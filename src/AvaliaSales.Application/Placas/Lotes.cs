using AvaliaSales.Domain;
using AvaliaSales.Domain.Placas;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Placas;

public sealed record LoteDto(Guid Id, string Rotulo, int Numero, int Quantidade, DateTimeOffset CriadoEm, string? Revendedor, DateTimeOffset? DespachadoEm, DateTimeOffset? AtivadoEm,
    string? Titulo = null, CategoriaDoLote Categoria = CategoriaDoLote.Placa, DateTimeOffset? ArquivadoEm = null, DateTimeOffset? BaixadoEm = null, bool PodeExcluir = false,
    string? CodigoDeAtivacao = null, DateTimeOffset? VendidoNoDigitalEm = null, long? ValorDoDigitalCents = null, CanalDigital? Canal = null, string? PedidoNoCanal = null);

/// <summary>A baixa digital das placas separadas para um pedido, com valor total, canal e revenda que fará a ativação.</summary>
public sealed record VendaDigitalDoLote(Guid RevendedorId, long ValorCents, CanalDigital Canal, IReadOnlyList<string> CodigosDasPlacas, string? PedidoNoCanal = null);
public sealed record VendaDigitalParcial(Guid GrupoId, string Lote, int Quantidade, string CodigoDeAtivacao);
public sealed record VendaDigitalDoLoteDto(Guid GrupoId, string CodigoDeAtivacao, int Quantidade);

/// <summary>A página dos lotes no formato comum, com quantos arquivados o filtro deixou de fora (para "Ver arquivados").</summary>
public sealed record PaginaDeLotes(IReadOnlyList<LoteDto> Itens, int Total, int? Proxima, int Arquivados);

/// <summary>Livres são os ainda não despachados e não arquivados; <paramref name="Quantidade"/> exige essa quantidade de placas.</summary>
public sealed record FiltroDeLotes(bool Arquivados = false, bool Livres = false, int? Quantidade = null, string? Busca = null);

public sealed record GerarLote(int Quantidade, string? Titulo = null, CategoriaDoLote Categoria = CategoriaDoLote.Placa);

public sealed record DescreverLote(string? Titulo, CategoriaDoLote Categoria);

public sealed record PlacaParaImprimir(string Codigo, long Sequencia);

public sealed record ImpressaoDoLote(string Rotulo, IReadOnlyList<PlacaParaImprimir> Placas);

/// <summary>
/// A tiragem de placas pela operadora: número do lote em sequência, códigos
/// sorteados sem repetir nenhum já emitido nem reservado, e a sequência da
/// plataforma contínua entre lotes.
/// </summary>
public sealed class Lotes(IBancoDeDados banco, TimeProvider relogio, IGravacaoNaRevenda gravacao, Revendedores.AcoesSensiveisDaOperadora sensiveis)
{
    private const int RodadasDeSorteio = 20;

    /// <summary>Do número mais alto ao mais baixo. A busca vale para o número, o título e o nome do revendedor.</summary>
    public async Task<PaginaDeLotes> Listar(FiltroDeLotes filtro, PedidoDePagina pedido, CancellationToken ct)
    {
        var consulta = banco.Lotes.AsNoTracking();
        var termo = Paginacao.Termo(filtro.Busca);
        if (termo.Length > 0)
        {
            int? numero = int.TryParse(new string(termo.Where(char.IsAsciiDigit).ToArray()), out var lido) ? lido : null;
            consulta = consulta.Where(l => (l.Titulo != null && Busca.Normalizar(l.Titulo).Contains(Busca.Normalizar(termo)))
                || (numero != null && l.Numero == numero)
                || banco.Revendedores.Any(r => r.Id == l.RevendedorId && Busca.Normalizar(r.Nome).Contains(Busca.Normalizar(termo))));
        }

        if (filtro.Livres)
        {
            consulta = consulta.Where(l => l.DespachadoEm == null);
        }

        if (filtro.Quantidade is { } quantidade)
        {
            consulta = consulta.Where(l => l.Quantidade == quantidade);
        }

        var arquivados = await consulta.CountAsync(l => l.ArquivadoEm != null, ct);
        if (!filtro.Arquivados || filtro.Livres)
        {
            consulta = consulta.Where(l => l.ArquivadoEm == null);
        }

        var pagina = await consulta.OrderByDescending(l => l.Numero).ThenByDescending(l => l.Id).PaginarAsync(pedido, ct);
        var lotes = pagina.Itens;
        var nomes = await banco.Revendedores.AsNoTracking()
            .Where(r => lotes.Select(l => l.RevendedorId).Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Nome, ct);
        return new PaginaDeLotes(lotes.Select(l => Dto(l, l.RevendedorId is { } r ? nomes.GetValueOrDefault(r) : null)).ToList(), pagina.Total, pagina.Proxima, arquivados);
    }

    private static LoteDto Dto(Lote l, string? revendedor) => new(l.Id, l.Rotulo, l.Numero, l.Quantidade, l.CriadoEm, revendedor,
        l.DespachadoEm, l.AtivadoEm, l.Titulo, l.Categoria, l.ArquivadoEm, l.BaixadoEm, l.PodeExcluir,
        l.CodigoDeAtivacao, l.VendidoNoDigitalEm, l.ValorDoDigitalCents, l.Canal, l.PedidoNoCanal);

    public Task<LoteDto> Gerar(int quantidade, CancellationToken ct) => Gerar(new GerarLote(quantidade), ct);

    public async Task<LoteDto> Gerar(GerarLote pedido, CancellationToken ct)
    {
        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        await banco.TravarGeracaoDeLotes(ct);

        var numero = (await banco.Lotes.MaxAsync(l => (int?)l.Numero, ct) ?? 0) + 1;
        var lote = new Lote(numero, pedido.Quantidade, relogio.GetUtcNow(), pedido.Titulo, pedido.Categoria);
        var sequencia = await banco.PlacasEmitidas.MaxAsync(p => (long?)p.Sequencia, ct) ?? 0;

        foreach (var codigo in await SortearLivres(banco, pedido.Quantidade, ct))
        {
            banco.PlacasEmitidas.Add(new PlacaEmitida(codigo, ++sequencia, lote.Id));
        }

        banco.Lotes.Add(lote);
        await banco.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
        return Dto(lote, null);
    }

    /// <summary>
    /// O código do lote para o comprador: sorteado como o da placa e fora de todos os códigos de lote, de placa e
    /// reservados, para nunca ser lido como outra coisa. Quem chama segura a trava da geração de lotes; o índice único é
    /// a última palavra.
    /// </summary>
    internal static async Task<string> SortearCodigoDeAtivacao(IBancoDeDados banco, CancellationToken ct)
    {
        for (var rodada = 0; rodada < RodadasDeSorteio; rodada++)
        {
            var codigo = CodigoDaPlaca.Sortear();
            var usado = await banco.Lotes.AnyAsync(l => l.CodigoDeAtivacao == codigo, ct)
                || await banco.PlacasEmitidas.AnyAsync(p => p.CodigoDeAtivacao == codigo, ct)
                || await banco.PlacasEmitidas.AnyAsync(p => p.Codigo == codigo, ct)
                || await banco.CodigosReservados.AnyAsync(c => c.Codigo == codigo, ct);
            if (!usado)
            {
                return codigo;
            }
        }

        throw new InvalidOperationException("Não sobrou código de ativação livre depois de 20 rodadas.");
    }

    /// <summary>
    /// A baixa manual da venda digital: o lote livre vai para a revenda escolhida, as placas entram no estoque dela sem
    /// custo de pedido e reservadas ao comprador, e o valor recebido no canal entra no razão dela como receita, numa linha
    /// só, no dia da baixa. O comprador ativa depois pelo código do lote, sem passar pelo balcão.
    /// </summary>
    public async Task<LoteDto> VenderNoDigital(Guid loteId, VendaDigitalDoLote pedido, CancellationToken ct)
    {
        await sensiveis.Exigir("Lançar a venda digital do lote", ct);
        return await gravacao.Gravar(pedido.RevendedorId, async b =>
        {
            var revenda = await b.Revendedores.SingleAsync(r => r.Id == pedido.RevendedorId, ct);
            revenda.ExigirAtiva();
            var lote = await b.TravarLotePorId(loteId, ct) ?? throw new RegraDeNegocioException("Lote não encontrado.");
            await b.TravarGeracaoDeLotes(ct);
            var agora = relogio.GetUtcNow();
            lote.VenderNoDigital(pedido.RevendedorId, pedido.ValorCents, pedido.Canal, pedido.PedidoNoCanal, agora);
            foreach (var emitida in await b.PlacasEmitidas.Where(p => p.LoteId == lote.Id).OrderBy(p => p.Sequencia).ToListAsync(ct))
            {
                emitida.EntregarA(pedido.RevendedorId);
                // Sem custo de pedido: a revenda não comprou o pacote, a produção é da Avalia.
                b.Placas.Add(new Placa(emitida, 0, agora));
            }

            var onde = lote.PedidoNoCanal is { } numero ? $"{NomeDoCanal(lote.Canal!.Value)}, pedido {numero}" : NomeDoCanal(lote.Canal!.Value);
            b.Lancamentos.Add(new Domain.Financeiro.Lancamento(Domain.Financeiro.TipoDeLancamento.Receita, lote.Id, pedido.ValorCents,
                $"Venda digital do lote {lote.Rotulo} ({onde})", agora));
            b.AvisarAoVivo("estoque", pedido.RevendedorId, operadora: true);
            return Dto(lote, revenda.Nome);
        }, ct);
    }

    /// <summary>Separa apenas as placas do pedido e cria um acesso exclusivo para esse grupo.</summary>
    public async Task<VendaDigitalParcial> VenderPlacasNoDigital(Guid loteId, VendaDigitalDoLote pedido, CancellationToken ct)
    {
        await sensiveis.Exigir("Lançar venda digital de placas", ct);
        var codigos = pedido.CodigosDasPlacas?.Select(CodigoDaPlaca.Normalizar).ToList() ?? [];
        if (codigos.Count == 0 || codigos.Any(c => c.Length == 0) || codigos.Distinct().Count() != codigos.Count)
            throw new RegraDeNegocioException("Informe códigos de placas válidos, sem repetição.");

        return await gravacao.Gravar(pedido.RevendedorId, async b =>
        {
            var revenda = await b.Revendedores.SingleAsync(r => r.Id == pedido.RevendedorId, ct);
            revenda.ExigirAtiva();
            var lote = await b.TravarLotePorId(loteId, ct) ?? throw new RegraDeNegocioException("Lote não encontrado.");
            if (lote.ArquivadoEm is not null || lote.Categoria is not (CategoriaDoLote.Placa or CategoriaDoLote.Cardapio))
                throw new RegraDeNegocioException("Lote indisponível para venda digital.");
            if (codigos.Count > lote.Quantidade) throw new RegraDeNegocioException($"Lote {lote.Rotulo} tem {lote.Quantidade} placas.");
            if (pedido.ValorCents <= 0 || !Enum.IsDefined(pedido.Canal)) throw new RegraDeNegocioException("Informe o valor e o canal da venda.");

            var emitidas = await b.PlacasEmitidas.Where(p => p.LoteId == lote.Id && codigos.Contains(p.Codigo)).ToListAsync(ct);
            if (emitidas.Count != codigos.Count || emitidas.Any(p => p.RevendedorId is not null && p.RevendedorId != pedido.RevendedorId || p.CodigoDeAtivacao is not null))
                throw new RegraDeNegocioException("Há códigos fora do lote ou já separados para outra venda.");

            var agora = relogio.GetUtcNow();
            var codigoAtivacao = await SortearCodigoDeAtivacao(b, ct);
            var grupo = new GrupoDeAtivacao(pedido.RevendedorId, lote.Id, codigoAtivacao, pedido.ValorCents, pedido.Canal, pedido.PedidoNoCanal, agora);
            b.GruposDeAtivacao.Add(grupo);
            var codigosExistentes = await b.Placas.Where(p => codigos.Contains(p.Codigo)).ToDictionaryAsync(p => p.Codigo, ct);
            if (codigosExistentes.Values.Any(p => p.Situacao != SituacaoDaPlaca.EmEstoque || p.VendaId is not null))
                throw new RegraDeNegocioException("Há placas ativadas ou fora de estoque. Confira os códigos.");
            foreach (var emitida in emitidas)
            {
                emitida.VincularAoGrupo(pedido.RevendedorId, grupo.Id, codigoAtivacao);
                if (!codigosExistentes.ContainsKey(emitida.Codigo)) b.Placas.Add(new Placa(emitida, 0, agora));
            }

            var onde = pedido.PedidoNoCanal is { Length: > 0 } numero ? $"{NomeDoCanal(pedido.Canal)}, pedido {numero}" : NomeDoCanal(pedido.Canal);
            b.Lancamentos.Add(new Domain.Financeiro.Lancamento(Domain.Financeiro.TipoDeLancamento.Receita, grupo.Id, pedido.ValorCents,
                $"Venda digital de {codigos.Count} placas do lote {lote.Rotulo} ({onde})", agora));
            b.AvisarAoVivo("estoque", pedido.RevendedorId, operadora: true);
            await b.SaveChangesAsync(ct);
            return new VendaDigitalParcial(grupo.Id, lote.Rotulo, emitidas.Count, codigoAtivacao);
        }, ct);
    }

    /// <summary>Devolução antes da ativação: revoga o link, desfaz a receita e devolve as placas ao estoque da mesma revenda.</summary>
    public async Task CancelarVendaDigital(Guid grupoId, CancellationToken ct)
    {
        await sensiveis.Exigir("Registrar devolução da venda digital", ct);
        var revendedorId = await banco.PlacasEmitidas.AsNoTracking()
            .Where(p => p.GrupoDeAtivacaoId == grupoId && p.RevendedorId != null)
            .Select(p => p.RevendedorId).FirstOrDefaultAsync(ct)
            ?? throw new RegraDeNegocioException("Venda digital não encontrada.");
        await gravacao.Gravar(revendedorId, async b =>
        {
            var atual = await b.GruposDeAtivacao.SingleAsync(g => g.Id == grupoId, ct);
            if (atual.CanceladoEm is not null) return true;
            _ = await b.TravarLotePorId(atual.LoteId, ct) ?? throw new RegraDeNegocioException("Venda digital não encontrada.");
            if ((await b.GruposDeAtivacao.SingleAsync(g => g.Id == grupoId, ct)).CanceladoEm is not null) return true;
            var emitidas = await b.PlacasEmitidas.Where(p => p.GrupoDeAtivacaoId == grupoId).ToListAsync(ct);
            var placas = await b.Placas.Where(p => emitidas.Select(e => e.Codigo).Contains(p.Codigo)).ToListAsync(ct);
            if (placas.Count != emitidas.Count || placas.Any(p => p.VendaId is null && p.Situacao != SituacaoDaPlaca.EmEstoque))
                throw new RegraDeNegocioException("Há placa fora do estoque. Confira o grupo antes da devolução.");
            var receita = await b.Lancamentos.SingleAsync(l => l.OrigemId == grupoId && l.Tipo == Domain.Financeiro.TipoDeLancamento.Receita && l.EstornaId == null, ct);
            var agora = relogio.GetUtcNow();
            foreach (var placa in placas.Where(p => p.VendaId is not null))
            {
                var venda = await b.Vendas.SingleAsync(v => v.Id == placa.VendaId && v.DoCanalDigital, ct);
                venda.Cancelar(Domain.Vendas.MotivoDoCancelamento.Devolucao, "Devolução do pedido digital", agora);
                placa.DesfazerVenda();
                placa.Baixar(MotivoDaBaixa.Devolucao, agora);
            }

            atual.Cancelar(agora);
            foreach (var emitida in emitidas) emitida.LiberarDoGrupo(grupoId);
            b.Lancamentos.Add(receita.Estorno("devolução do pedido digital", agora));
            b.AvisarAoVivo("estoque", revendedorId, operadora: true);
            return true;
        }, ct);
    }

    /// <summary>Grupos ativos do lote, apenas com os identificadores necessários para reenviar ou revogar o acesso.</summary>
    public async Task<IReadOnlyList<VendaDigitalDoLoteDto>> VendasDigitais(Guid loteId, CancellationToken ct)
    {
        var existe = await banco.Lotes.AsNoTracking().AnyAsync(l => l.Id == loteId, ct);
        if (!existe) throw new RegraDeNegocioException("Lote não encontrado.");
        return await banco.PlacasEmitidas.AsNoTracking()
            .Where(p => p.LoteId == loteId && p.GrupoDeAtivacaoId != null && p.CodigoDeAtivacao != null)
            .GroupBy(p => new { GrupoId = p.GrupoDeAtivacaoId!.Value, Codigo = p.CodigoDeAtivacao! })
            .Select(g => new VendaDigitalDoLoteDto(g.Key.GrupoId, g.Key.Codigo, g.Count()))
            .ToListAsync(ct);
    }

    public static string NomeDoCanal(CanalDigital canal) => canal switch
    {
        CanalDigital.MercadoLivre => "Mercado Livre",
        CanalDigital.TikTokShop => "TikTok Shop",
        CanalDigital.Shopee => "Shopee",
        _ => "outro canal",
    };

    public async Task<LoteDto> Descrever(Guid loteId, DescreverLote pedido, CancellationToken ct)
    {
        var lote = await Achar(loteId, ct);
        lote.Descrever(pedido.Titulo, pedido.Categoria);
        await banco.SaveChangesAsync(ct);
        return Dto(lote, null);
    }

    /// <summary>
    /// Lote que ainda não foi para revenda nenhuma. O que já está com a revenda
    /// tem placas dela, isoladas no banco: quem arquiva é o dono, pelo estoque
    /// (<see cref="Estoque.ArquivarLote"/>).
    /// </summary>
    public async Task Arquivar(Guid loteId, bool arquivar, CancellationToken ct)
    {
        var lote = await Achar(loteId, ct);
        if (lote.RevendedorId is not null)
        {
            throw new RegraDeNegocioException($"Lote {lote.Rotulo} já entregue à revenda. Só o revendedor arquiva, pelo estoque.");
        }

        if (arquivar && await banco.PlacasEmitidas.AnyAsync(p => p.LoteId == loteId && p.CodigoDeAtivacao != null, ct))
        {
            throw new RegraDeNegocioException($"Lote {lote.Rotulo} tem pedidos digitais ativos. Cancele-os antes de arquivar.");
        }

        if (arquivar)
        {
            lote.Arquivar(relogio.GetUtcNow());
        }
        else
        {
            lote.Desarquivar();
        }

        await banco.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Só lote que nunca teve o arquivo baixado nem foi despachado: antes disso
    /// nenhum código pode estar impresso. Os códigos ficam reservados, para o
    /// sorteio nunca devolvê-los.
    /// </summary>
    public async Task Excluir(Guid loteId, CancellationToken ct)
    {
        var lote = await Achar(loteId, ct);
        if (!lote.PodeExcluir)
        {
            throw new RegraDeNegocioException($"Lote {lote.Rotulo} com arquivo baixado ou despachado. Arquive em vez de excluir.");
        }

        var emitidas = await banco.PlacasEmitidas.Where(p => p.LoteId == loteId).ToListAsync(ct);
        banco.CodigosReservados.AddRange(emitidas.Select(p => new CodigoReservado(p.Codigo, $"Lote {lote.Rotulo} excluído")));
        banco.PlacasEmitidas.RemoveRange(emitidas);
        banco.Lotes.Remove(lote);
        await banco.SaveChangesAsync(ct);
    }

    private async Task<Lote> Achar(Guid loteId, CancellationToken ct) =>
        await banco.Lotes.SingleOrDefaultAsync(l => l.Id == loteId, ct) ?? throw new RegraDeNegocioException("Lote não encontrado.");

    /// <summary>Não guarda imagem: o QR é refeito do código, igual toda vez. O primeiro download trava a exclusão.</summary>
    public async Task<ImpressaoDoLote> Impressao(Guid loteId, CancellationToken ct)
    {
        var lote = await Achar(loteId, ct);
        if (lote.BaixadoEm is null)
        {
            lote.RegistrarDownload(relogio.GetUtcNow());
            await banco.SaveChangesAsync(ct);
        }

        var placas = await banco.PlacasEmitidas.AsNoTracking().Where(p => p.LoteId == loteId)
            .OrderBy(p => p.Sequencia).Select(p => new PlacaParaImprimir(p.Codigo, p.Sequencia)).ToListAsync(ct);
        return new ImpressaoDoLote(lote.Rotulo, placas);
    }

    /// <summary>Códigos que ainda não existem. A chave primária é a última palavra; isto evita a colisão antes dela.</summary>
    /// <summary>Códigos que nunca saíram nem estão reservados; quem chama segura a trava da geração de lotes.</summary>
    internal static async Task<IReadOnlyList<string>> SortearLivres(IBancoDeDados banco, int quantidade, CancellationToken ct)
    {
        var escolhidos = new HashSet<string>(StringComparer.Ordinal);
        for (var rodada = 0; rodada < RodadasDeSorteio && escolhidos.Count < quantidade; rodada++)
        {
            var candidatos = new HashSet<string>(StringComparer.Ordinal);
            while (candidatos.Count < quantidade - escolhidos.Count)
            {
                var codigo = CodigoDaPlaca.Sortear();
                if (!escolhidos.Contains(codigo))
                {
                    candidatos.Add(codigo);
                }
            }

            var usados = await banco.PlacasEmitidas.Where(p => candidatos.Contains(p.Codigo)).Select(p => p.Codigo)
                .Concat(banco.CodigosReservados.Where(c => candidatos.Contains(c.Codigo)).Select(c => c.Codigo))
                .ToListAsync(ct);
            candidatos.ExceptWith(usados);
            escolhidos.UnionWith(candidatos);
        }

        return escolhidos.Count == quantidade
            ? escolhidos.ToList()
            : throw new InvalidOperationException("Não sobrou código livre suficiente depois de 20 rodadas.");
    }
}
