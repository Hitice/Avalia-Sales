using AvaliaSales.Application.Equipe;
using AvaliaSales.Domain;
using AvaliaSales.Domain.Catalogo;
using AvaliaSales.Domain.Financeiro;
using AvaliaSales.Domain.Placas;
using AvaliaSales.Domain.Vendas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Application.Importacao;

/// <summary>Uma linha do arquivo de placas (formato em docs/OPERACAO.md, "Importar placas").</summary>
public sealed record LinhaDePlaca(
    int Linha,
    string Codigo,
    string Situacao,
    string? Destino,
    DateTimeOffset? VendidaEm,
    DateTimeOffset? VenceEm,
    long? ValorCents,
    long? CustoCents,
    string? ClienteNome,
    string? ClienteResponsavel,
    string? ClienteTelefone,
    string? ClienteDocumento,
    string? VendedorEmail,
    long TotalLeituras,
    string? Produto = null,
    long? ProdutoPrecoCents = null,
    long? ComissaoDevidaCents = null,
    string? ClienteEmail = null,
    string? ClienteLugarNoGoogle = null);

/// <summary>Os totais para bater com o relatório do sistema de origem, ao centavo.</summary>
public sealed record ResultadoDaImportacao(
    bool Gravada, int Linhas, int EmEstoque, int NoAr, int Suspensas, int Baixadas, int Vendidas,
    long ValorVendidoCents, long CustoCents, int VendedorNaoIdentificado, int ReservasLiberadas, IReadOnlyList<string> Recusas,
    long ComissaoDevidaCents = 0, int ProdutosCriados = 0);

/// <summary>Uma linha do arquivo de lotes de origem: o lote do sistema anterior em que o código foi impresso.</summary>
public sealed record LinhaDeLote(int Linha, string Codigo, string Lote, string? Titulo, CategoriaDoLote Categoria, DateTimeOffset? CriadoEm, bool Arquivar);

public sealed record ResultadoDosLotes(bool Gravado, int Lotes, int Placas, int Arquivados, int Baixadas, IReadOnlyList<string> Recusas);

/// <summary>O que o complemento dos clientes já importados preencheu; só campo vazio é preenchido.</summary>
public sealed record ResultadoDoComplemento(bool Gravado, int Linhas, int Clientes, int Emails, int Lugares, IReadOnlyList<string> Recusas);

/// <summary>
/// A importação das placas de outro sistema para um revendedor, como as do
/// Sales da Avalia. Tudo ou nada: uma linha recusada desfaz o arquivo inteiro,
/// e o ensaio desfaz sempre, só para conferir os totais. Serve a qualquer
/// revendedor que já venda placas.
/// </summary>
public sealed class Importacao(IBancoDeDados banco, IRevendedorAtual atual, IContas contas, IOptions<OpcoesDaPlataforma> plataforma, TimeProvider relogio)
{
    public const string ProdutoImportado = "Placa importada";

    private static readonly Dictionary<string, SituacaoDaPlaca> Situacoes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["em_branco"] = SituacaoDaPlaca.EmEstoque,
        ["ativa"] = SituacaoDaPlaca.Ativa,
        ["suspensa"] = SituacaoDaPlaca.Suspensa,
        ["baixada"] = SituacaoDaPlaca.Baixada,
    };

    public async Task<ResultadoDaImportacao> Importar(IReadOnlyList<LinhaDePlaca> linhas, bool ensaio, CancellationToken ct)
    {
        var revendedorId = atual.RevendedorId ?? throw new InvalidOperationException("Importação sem revendedor.");
        if (linhas.Count == 0)
        {
            throw new RegraDeNegocioException("O arquivo não tem placas.");
        }

        // Conta encerrada só consulta: a importação espera a reabertura aprovada pela operadora.
        if (await banco.Revendedores.AnyAsync(r => r.Id == revendedorId && r.EncerradaEm != null, ct))
        {
            throw new RegraDeNegocioException("A conta deste revendedor está encerrada. Reabra pela tela Revendedores antes de importar.");
        }

        var agora = relogio.GetUtcNow();
        var recusas = new List<string>();
        var codigos = linhas.Select(l => CodigoDaPlaca.Normalizar(l.Codigo)).ToList();
        var repetidos = codigos.Where(c => c.Length > 0).GroupBy(c => c).Where(g => g.Count() > 1).ToList();
        foreach (var repetido in repetidos)
        {
            recusas.Add($"O código {repetido.Key} aparece {repetido.Count()} vezes no arquivo.");
        }

        var codigosRepetidos = repetidos.Select(g => g.Key).ToHashSet(StringComparer.Ordinal);

        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        await banco.TravarGeracaoDeLotes(ct);
        var jaEmitidos = await banco.PlacasEmitidas.Where(p => codigos.Contains(p.Codigo)).Select(p => p.Codigo).ToListAsync(ct);
        var donos = await contas.EmailsDosDonos(revendedorId, ct);
        var dono = donos.Count > 0 ? await contas.IdPorEmail(donos[0], ct) : null;
        var daPlataforma = await banco.ProdutosDaPlataforma.AsNoTracking().ToDictionaryAsync(p => p.Chave, StringComparer.OrdinalIgnoreCase, ct);
        var daRevenda = await banco.Produtos.Where(p => p.ProdutoDaPlataformaId != null).ToDictionaryAsync(p => p.ProdutoDaPlataformaId!.Value, ct);
        var generico = await banco.Produtos.FirstOrDefaultAsync(p => p.Nome == ProdutoImportado && p.ProdutoDaPlataformaId == null, ct);
        var vendedoresDaEquipe = (await banco.Vendedores.Select(v => v.Id).ToListAsync(ct)).ToHashSet();
        var produtosCriados = 0;
        long comissaoDevida = 0;

        var numero = (await banco.Lotes.MaxAsync(l => (int?)l.Numero, ct) ?? 0) + 1;
        var sequencia = await banco.PlacasEmitidas.MaxAsync(p => (long?)p.Sequencia, ct) ?? 0;
        var lote = Lote.Importado(numero, linhas.Count, revendedorId, agora);
        banco.Lotes.Add(lote);
        var clientes = new Dictionary<string, Cliente>(StringComparer.Ordinal);
        var vendedores = new Dictionary<string, Guid?>(StringComparer.OrdinalIgnoreCase);
        int emEstoque = 0, noAr = 0, suspensas = 0, baixadas = 0, vendidas = 0, semVendedor = 0;
        long valor = 0, custo = 0;

        foreach (var (linha, codigo) in linhas.Zip(codigos))
        {
            // Já recusado acima, uma vez para todas as linhas do código.
            if (codigosRepetidos.Contains(codigo))
            {
                continue;
            }

            try
            {
                if (codigo.Length == 0)
                {
                    throw new RegraDeNegocioException($"código inválido: {linha.Codigo}");
                }

                if (jaEmitidos.Contains(codigo))
                {
                    throw new RegraDeNegocioException($"o código {codigo} já existe na plataforma");
                }

                if (!Situacoes.TryGetValue(linha.Situacao.Trim(), out var situacao))
                {
                    throw new RegraDeNegocioException($"situação desconhecida: {linha.Situacao} (use em_branco, ativa, suspensa ou baixada)");
                }

                var emitida = new PlacaEmitida(codigo, ++sequencia, lote.Id);
                emitida.EntregarA(revendedorId);
                var custoDaPlaca = linha.CustoCents ?? 0;
                var destino = string.IsNullOrWhiteSpace(linha.Destino) ? null : Destino.Conferir(linha.Destino, plataforma.Value.HostPublico);
                var placa = Placa.Importada(emitida, custoDaPlaca, agora, situacao, destino, linha.VendidaEm, linha.VenceEm, linha.TotalLeituras);

                if (linha.VendidaEm is { } vendidaEm)
                {
                    var cliente = Cliente(linha, agora, clientes);
                    var vendedor = await Vendedor(linha.VendedorEmail, vendedores, ct);
                    if (vendedor is null)
                    {
                        semVendedor++;
                    }

                    var produto = Produto(linha, daPlataforma, daRevenda, ref generico, ref produtosCriados);
                    var venda = Venda.Importada(placa.Id, cliente.Id, produto.Id,
                        vendedor ?? dono ?? throw new RegraDeNegocioException("o revendedor não tem dono para receber as vendas sem vendedor"),
                        linha.ValorCents ?? 0, custoDaPlaca, vendidaEm);
                    placa.LigarVendaImportada(venda.Id);
                    banco.Vendas.Add(venda);

                    // Comissão de venda paga que o outro sistema ainda não repassou: vira saldo do vendedor aqui.
                    if (linha.ComissaoDevidaCents is > 0 and var devida)
                    {
                        if (vendedor is not { } quem || !vendedoresDaEquipe.Contains(quem))
                        {
                            throw new RegraDeNegocioException($"comissão devida sem vendedor da equipe ({linha.VendedorEmail})");
                        }

                        // Na data de hoje, e não na da venda: mês fechado do razão não muda.
                        banco.Lancamentos.Add(new Lancamento(TipoDeLancamento.ComissaoDevida, venda.Id, -devida,
                            $"Comissão da placa {codigo}, vendida em {vendidaEm.ToOffset(TimeSpan.FromHours(-3)).ToString("dd'/'MM'/'yyyy", System.Globalization.CultureInfo.InvariantCulture)} no sistema anterior", agora, venda.Id, quem));
                        comissaoDevida += devida;
                    }
                    vendidas++;
                    valor += venda.ValorCents;
                }

                banco.PlacasEmitidas.Add(emitida);
                banco.Placas.Add(placa);
                custo += custoDaPlaca;
                switch (situacao)
                {
                    case SituacaoDaPlaca.EmEstoque: emEstoque++; break;
                    case SituacaoDaPlaca.Ativa: noAr++; break;
                    case SituacaoDaPlaca.Suspensa: suspensas++; break;
                    case SituacaoDaPlaca.Baixada: baixadas++; break;
                }
            }
            catch (RegraDeNegocioException recusa)
            {
                recusas.Add($"Linha {linha.Linha}: {recusa.Message}");
            }
        }

        // O código que vem para a plataforma deixa de ser reserva: agora é placa daqui.
        var reservas = await banco.CodigosReservados.Where(c => codigos.Contains(c.Codigo)).ToListAsync(ct);
        banco.CodigosReservados.RemoveRange(reservas);

        var gravar = recusas.Count == 0 && !ensaio;
        if (recusas.Count == 0)
        {
            await banco.SaveChangesAsync(ct);
        }

        if (gravar)
        {
            await transacao.CommitAsync(ct);
        }
        else
        {
            await transacao.RollbackAsync(ct);
        }

        return new ResultadoDaImportacao(gravar, linhas.Count, emEstoque, noAr, suspensas, baixadas, vendidas, valor, custo, semVendedor, reservas.Count, recusas,
            comissaoDevida, produtosCriados);
    }

    /// <summary>
    /// O produto da venda pela chave do catálogo da Avalia (a mesma do Sales).
    /// A revenda que ainda não tinha o produto passa a ter, com o preço do
    /// arquivo; sem chave, a venda vai para o produto genérico, fora do catálogo.
    /// </summary>
    private Produto Produto(LinhaDePlaca linha, Dictionary<string, ProdutoDaPlataforma> daPlataforma, Dictionary<Guid, Produto> daRevenda,
        ref Produto? generico, ref int criados)
    {
        if (string.IsNullOrWhiteSpace(linha.Produto))
        {
            if (generico is null)
            {
                generico = new Produto(ProdutoImportado, 0);
                generico.DefinirAtivo(false);
                banco.Produtos.Add(generico);
            }

            return generico;
        }

        var doCatalogo = daPlataforma.GetValueOrDefault(linha.Produto.Trim())
            ?? throw new RegraDeNegocioException($"o produto {linha.Produto} não está no catálogo da Avalia");
        if (daRevenda.TryGetValue(doCatalogo.Id, out var existente))
        {
            return existente;
        }

        var novo = Domain.Catalogo.Produto.DaPlataforma(doCatalogo);
        if (linha.ProdutoPrecoCents is { } preco)
        {
            novo.Alterar(novo.Nome, preco, null);
        }

        banco.Produtos.Add(novo);
        daRevenda[doCatalogo.Id] = novo;
        criados++;
        return novo;
    }

    /// <summary>Um cliente por documento, senão por telefone, senão por nome, dentro do arquivo e do que já existe.</summary>
    private Cliente Cliente(LinhaDePlaca linha, DateTimeOffset agora, Dictionary<string, Cliente> doArquivo)
    {
        var novo = Domain.Vendas.Cliente.Importado(linha.ClienteNome ?? "Cliente sem nome", linha.ClienteResponsavel, linha.ClienteTelefone, linha.ClienteDocumento, agora);
        var chave = novo.Documento ?? (novo.Telefone.Length > 0 ? "tel:" + novo.Telefone : "nome:" + novo.Nome.ToUpperInvariant());
        if (doArquivo.TryGetValue(chave, out var existente))
        {
            return existente;
        }

        var doBanco = novo.Documento is { } documento
            ? banco.Clientes.FirstOrDefault(c => c.Documento == documento)
            : novo.Telefone.Length > 0 ? banco.Clientes.FirstOrDefault(c => c.Telefone == novo.Telefone) : null;
        if (doBanco is null)
        {
            banco.Clientes.Add(novo);
        }

        doArquivo[chave] = doBanco ?? novo;
        doArquivo[chave].Completar(null, linha.ClienteEmail, linha.ClienteLugarNoGoogle);
        return doArquivo[chave];
    }

    /// <summary>
    /// As placas importadas num lote só voltam para os lotes em que foram
    /// impressas no sistema anterior, com o título e a categoria de lá; o lote de
    /// teste sai arquivado. Tudo ou nada, com ensaio. Placa que já está num lote
    /// com título (já recriado) é recusada, para não mover duas vezes.
    /// </summary>
    public async Task<ResultadoDosLotes> RecriarLotes(IReadOnlyList<LinhaDeLote> linhas, bool ensaio, CancellationToken ct)
    {
        var revendedorId = atual.RevendedorId ?? throw new InvalidOperationException("Lotes sem revendedor.");
        var agora = relogio.GetUtcNow();
        var recusas = new List<string>();
        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        await banco.TravarGeracaoDeLotes(ct);
        var numero = await banco.Lotes.MaxAsync(l => (int?)l.Numero, ct) ?? 0;
        int lotes = 0, placas = 0, arquivados = 0, baixadas = 0;
        var deOndeSairam = new HashSet<Guid>();

        foreach (var grupo in linhas.GroupBy(l => l.Lote.Trim()).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var codigos = grupo.Select(l => CodigoDaPlaca.Normalizar(l.Codigo)).ToList();
            var doGrupo = await banco.Placas.Where(p => codigos.Contains(p.Codigo)).ToListAsync(ct);
            foreach (var faltando in codigos.Except(doGrupo.Select(p => p.Codigo)))
            {
                recusas.Add($"{grupo.Key}: a placa {faltando} não está nesta revenda");
            }

            var lotesAtuais = doGrupo.Select(p => p.LoteId).Distinct().ToList();
            if (await banco.Lotes.AnyAsync(l => lotesAtuais.Contains(l.Id) && l.Titulo != null, ct))
            {
                recusas.Add($"{grupo.Key}: as placas já estão num lote com título");
                continue;
            }

            var primeira = grupo.First();
            var titulo = $"Sales {grupo.Key}" + (string.IsNullOrWhiteSpace(primeira.Titulo) ? string.Empty : $" - {primeira.Titulo.Trim()}");
            var lote = Lote.DeOrigem(++numero, doGrupo.Count > 0 ? doGrupo.Count : 1, revendedorId,
                titulo.Length <= Lote.TituloMaximo ? titulo : titulo[..Lote.TituloMaximo], primeira.Categoria, primeira.CriadoEm ?? agora, agora);
            banco.Lotes.Add(lote);
            var emitidas = await banco.PlacasEmitidas.Where(p => codigos.Contains(p.Codigo)).ToListAsync(ct);
            foreach (var placa in doGrupo)
            {
                deOndeSairam.Add(placa.LoteId);
                placa.MoverParaOLote(lote.Id);
            }

            foreach (var emitida in emitidas)
            {
                emitida.MoverParaOLote(lote.Id);
            }

            if (primeira.Arquivar)
            {
                lote.Arquivar(agora);
                arquivados++;
                foreach (var livre in doGrupo.Where(p => p.Situacao == SituacaoDaPlaca.EmEstoque && p.VendaId == null))
                {
                    livre.Baixar(MotivoDaBaixa.LoteArquivado, agora);
                    baixadas++;
                }
            }

            lotes++;
            placas += doGrupo.Count;
        }

        if (recusas.Count == 0)
        {
            await banco.SaveChangesAsync(ct);

            // O lote único da importação que ficou vazio deixa de existir.
            var vazios = await banco.Lotes.Where(l => deOndeSairam.Contains(l.Id) && l.Titulo == null
                && !banco.PlacasEmitidas.Any(p => p.LoteId == l.Id)).ToListAsync(ct);
            banco.Lotes.RemoveRange(vazios);
            await banco.SaveChangesAsync(ct);
        }

        var gravar = recusas.Count == 0 && !ensaio;
        if (gravar)
        {
            await transacao.CommitAsync(ct);
        }
        else
        {
            await transacao.RollbackAsync(ct);
        }

        return new ResultadoDosLotes(gravar, lotes, placas, arquivados, baixadas, recusas);
    }

    /// <summary>
    /// Completa e-mail e lugar no Google dos clientes de placas já importadas,
    /// achados pelo código da placa vendida. Não sobrescreve nada e pode rodar de
    /// novo; o ensaio desfaz sempre.
    /// </summary>
    public async Task<ResultadoDoComplemento> CompletarClientes(IReadOnlyList<LinhaDePlaca> linhas, bool ensaio, CancellationToken ct)
    {
        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        var recusas = new List<string>();
        var vistos = new HashSet<Guid>();
        int emails = 0, lugares = 0;
        foreach (var linha in linhas.Where(l => !string.IsNullOrWhiteSpace(l.ClienteEmail) || !string.IsNullOrWhiteSpace(l.ClienteLugarNoGoogle)))
        {
            var codigo = CodigoDaPlaca.Normalizar(linha.Codigo);
            var clienteId = await (
                from p in banco.Placas
                join v in banco.Vendas on p.VendaId equals v.Id
                where p.Codigo == codigo
                select (Guid?)v.ClienteId).FirstOrDefaultAsync(ct);
            if (clienteId is not { } id)
            {
                recusas.Add($"Linha {linha.Linha}: a placa {linha.Codigo} não tem venda aqui");
                continue;
            }

            var cliente = await banco.Clientes.SingleAsync(c => c.Id == id, ct);
            var (tinhaEmail, tinhaLugar) = (cliente.Email is not null, cliente.LugarNoGoogle is not null);
            try
            {
                cliente.Completar(null, linha.ClienteEmail, linha.ClienteLugarNoGoogle);
            }
            catch (RegraDeNegocioException recusa)
            {
                recusas.Add($"Linha {linha.Linha}: {recusa.Message}");
                continue;
            }

            emails += !tinhaEmail && cliente.Email is not null ? 1 : 0;
            lugares += !tinhaLugar && cliente.LugarNoGoogle is not null ? 1 : 0;
            vistos.Add(id);
        }

        var gravar = recusas.Count == 0 && !ensaio;
        await banco.SaveChangesAsync(ct);
        if (gravar)
        {
            await transacao.CommitAsync(ct);
        }
        else
        {
            await transacao.RollbackAsync(ct);
        }

        return new ResultadoDoComplemento(gravar, linhas.Count, vistos.Count, emails, lugares, recusas);
    }

    private async Task<Guid?> Vendedor(string? email, Dictionary<string, Guid?> achados, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        if (!achados.TryGetValue(email.Trim(), out var id))
        {
            id = await contas.IdPorEmail(email.Trim(), ct);
            achados[email.Trim()] = id;
        }

        return id;
    }
}
