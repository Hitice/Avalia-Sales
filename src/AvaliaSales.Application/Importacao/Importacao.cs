using AvaliaSales.Application.Equipe;
using AvaliaSales.Domain;
using AvaliaSales.Domain.Catalogo;
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
    long TotalLeituras);

/// <summary>Os totais para bater com o relatório do sistema de origem, ao centavo.</summary>
public sealed record ResultadoDaImportacao(
    bool Gravada, int Linhas, int EmEstoque, int NoAr, int Suspensas, int Baixadas, int Vendidas,
    long ValorVendidoCents, long CustoCents, int VendedorNaoIdentificado, int ReservasLiberadas, IReadOnlyList<string> Recusas);

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
        var produto = await banco.Produtos.FirstOrDefaultAsync(p => p.Nome == ProdutoImportado, ct);
        if (produto is null)
        {
            produto = new Produto(ProdutoImportado, 0);
            produto.DefinirAtivo(false);
            banco.Produtos.Add(produto);
        }

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

                    var venda = Venda.Importada(placa.Id, cliente.Id, produto.Id,
                        vendedor ?? dono ?? throw new RegraDeNegocioException("o revendedor não tem dono para receber as vendas sem vendedor"),
                        linha.ValorCents ?? 0, custoDaPlaca, vendidaEm);
                    placa.LigarVendaImportada(venda.Id);
                    banco.Vendas.Add(venda);
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

        return new ResultadoDaImportacao(gravar, linhas.Count, emEstoque, noAr, suspensas, baixadas, vendidas, valor, custo, semVendedor, reservas.Count, recusas);
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
        return doArquivo[chave];
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
