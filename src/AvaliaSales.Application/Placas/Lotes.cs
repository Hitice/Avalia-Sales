using AvaliaSales.Domain;
using AvaliaSales.Domain.Placas;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Placas;

public sealed record LoteDto(Guid Id, string Rotulo, int Numero, int Quantidade, DateTimeOffset CriadoEm, string? Revendedor, DateTimeOffset? DespachadoEm, DateTimeOffset? AtivadoEm,
    string? Titulo = null, CategoriaDoLote Categoria = CategoriaDoLote.Placa, DateTimeOffset? ArquivadoEm = null, DateTimeOffset? BaixadoEm = null, bool PodeExcluir = false,
    string? CodigoDeAtivacao = null, DateTimeOffset? VendidoNoDigitalEm = null, long? ValorDoDigitalCents = null, CanalDigital? Canal = null, string? PedidoNoCanal = null, int NoDigital = 0);

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
        // Placas do lote livre que já saíram em pedidos digitais: o lote não é mais inteiro.
        var ids = lotes.Where(l => l.RevendedorId is null).Select(l => l.Id).ToList();
        var noDigital = await banco.PlacasEmitidas.AsNoTracking().Where(e => ids.Contains(e.LoteId) && e.RevendedorId != null)
            .GroupBy(e => e.LoteId).Select(g => new { g.Key, Quantas = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Quantas, ct);
        return new PaginaDeLotes(lotes.Select(l => Dto(l, l.RevendedorId is { } r ? nomes.GetValueOrDefault(r) : null) with { NoDigital = noDigital.GetValueOrDefault(l.Id) }).ToList(),
            pagina.Total, pagina.Proxima, arquivados);
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
            // Lote a caminho da revenda: as placas entram no estoque dela na ativação da caixa, e separar uma agora a quebraria.
            if (lote.DespachadoEm is not null && lote.AtivadoEm is null)
                throw new RegraDeNegocioException($"Lote {lote.Rotulo} despachado e ainda não ativado pela revenda.");
            // Lote vendido inteiro no fluxo antigo, com o código ainda valendo: as placas já são do comprador dele.
            if (lote.DaVendaDigital && lote.CodigoDeAtivacao is not null)
                throw new RegraDeNegocioException($"Lote {lote.Rotulo} vendido inteiro no digital.");
            if (codigos.Count > lote.Quantidade) throw new RegraDeNegocioException($"Lote {lote.Rotulo} tem {lote.Quantidade} placas.");
            if (pedido.ValorCents <= 0 || !Enum.IsDefined(pedido.Canal)) throw new RegraDeNegocioException("Informe o valor e o canal da venda.");

            var emitidas = await b.PlacasEmitidas.Where(p => p.LoteId == lote.Id && codigos.Contains(p.Codigo)).ToListAsync(ct);
            if (emitidas.Count != codigos.Count || emitidas.Any(p => p.RevendedorId is not null && p.RevendedorId != pedido.RevendedorId || p.CodigoDeAtivacao is not null))
                throw new RegraDeNegocioException("Há códigos fora do lote ou já separados para outra venda.");

            var agora = relogio.GetUtcNow();
            var codigosExistentes = await b.Placas.Where(p => codigos.Contains(p.Codigo)).ToDictionaryAsync(p => p.Codigo, ct);
            if (codigosExistentes.Values.Any(p => p.Situacao != SituacaoDaPlaca.EmEstoque || p.VendaId is not null))
                throw new RegraDeNegocioException("Há placas ativadas ou fora de estoque. Confira os códigos.");
            var codigoAtivacao = await SortearCodigoDeAtivacao(b, ct);
            // O custo vai junto da receita na DRE: a placa que já era da revenda leva o preço que ela pagou no pedido.
            var grupo = new GrupoDeAtivacao(pedido.RevendedorId, lote.Id, codigoAtivacao, pedido.ValorCents, pedido.Canal, pedido.PedidoNoCanal, agora,
                emitidas.Count, codigosExistentes.Values.Sum(p => p.CustoCents));
            b.GruposDeAtivacao.Add(grupo);
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

    /// <summary>
    /// Devolução do pedido digital: revoga o código, estorna a receita do grupo e devolve as placas não ativadas ao estoque
    /// da mesma revenda; a placa já ativada tem a venda cancelada e sai de circulação, porque o QR dela já está com alguém.
    /// O código fica reservado, para o sorteio nunca entregá-lo a outro comprador.
    /// </summary>
    public async Task CancelarVendaDigital(Guid grupoId, CancellationToken ct)
    {
        await sensiveis.Exigir("Registrar devolução da venda digital", ct);
        var revendedorId = await banco.PlacasEmitidas.AsNoTracking()
            .Where(p => p.GrupoDeAtivacaoId == grupoId && p.RevendedorId != null)
            .Select(p => p.RevendedorId).FirstOrDefaultAsync(ct)
            ?? throw new RegraDeNegocioException("Venda digital não encontrada.");
        await gravacao.Gravar(revendedorId, async b =>
        {
            var loteId = await b.GruposDeAtivacao.AsNoTracking().Where(g => g.Id == grupoId).Select(g => (Guid?)g.LoteId).SingleOrDefaultAsync(ct)
                ?? throw new RegraDeNegocioException("Venda digital não encontrada.");
            // Lido depois da trava do lote: duas devoluções ao mesmo tempo estornam uma vez só.
            _ = await b.TravarLotePorId(loteId, ct) ?? throw new RegraDeNegocioException("Venda digital não encontrada.");
            var grupo = await b.GruposDeAtivacao.SingleAsync(g => g.Id == grupoId, ct);
            if (grupo.CanceladoEm is not null) return true;
            var emitidas = await b.PlacasEmitidas.Where(p => p.GrupoDeAtivacaoId == grupoId && p.CodigoDeAtivacao != null).ToListAsync(ct);
            var agora = relogio.GetUtcNow();
            await DesfazerVendaDigital(b, emitidas.Select(e => e.Codigo).ToList(), grupoId, revendedorId, agora, ct);
            grupo.Cancelar(agora);
            foreach (var emitida in emitidas) emitida.LiberarDoGrupo(grupoId);
            b.CodigosReservados.Add(new CodigoReservado(grupo.Codigo, "Grupo de ativação devolvido"));
            return true;
        }, ct);
    }

    /// <summary>A mesma devolução para o lote vendido inteiro pelo fluxo antigo (ADR 0033), que o comprador ativa com o código do lote.</summary>
    public async Task DevolverLoteDigital(Guid loteId, CancellationToken ct)
    {
        await sensiveis.Exigir("Registrar devolução da venda digital", ct);
        var revendedorId = await banco.Lotes.AsNoTracking()
            .Where(l => l.Id == loteId && l.VendidoNoDigitalEm != null && l.CodigoDeAtivacao != null)
            .Select(l => l.RevendedorId).SingleOrDefaultAsync(ct)
            ?? throw new RegraDeNegocioException("Venda digital não encontrada.");
        await gravacao.Gravar(revendedorId, async b =>
        {
            var lote = await b.TravarLotePorId(loteId, ct) ?? throw new RegraDeNegocioException("Venda digital não encontrada.");
            if (lote.CodigoDeAtivacao is not { } codigo) return true;
            var codigos = await b.PlacasEmitidas.Where(p => p.LoteId == loteId).Select(p => p.Codigo).ToListAsync(ct);
            await DesfazerVendaDigital(b, codigos, loteId, revendedorId, relogio.GetUtcNow(), ct);
            lote.RevogarVendaDigital();
            b.CodigosReservados.Add(new CodigoReservado(codigo, $"Lote {lote.Rotulo} devolvido"));
            return true;
        }, ct);
    }

    private static async Task DesfazerVendaDigital(IBancoDeDados b, List<string> codigos, Guid origemDaReceita, Guid revendedorId, DateTimeOffset agora, CancellationToken ct)
    {
        var placas = await b.TravarPlacas(codigos, ct);
        if (placas.Count != codigos.Count || placas.Any(p => p.VendaId is null && p.Situacao != SituacaoDaPlaca.EmEstoque))
            throw new RegraDeNegocioException("Há placa fora do estoque. Confira as placas antes da devolução.");
        var receita = await b.Lancamentos.SingleAsync(l => l.OrigemId == origemDaReceita && l.Tipo == Domain.Financeiro.TipoDeLancamento.Receita && l.EstornaId == null, ct);
        foreach (var placa in placas.Where(p => p.VendaId is not null))
        {
            var venda = await b.Vendas.SingleAsync(v => v.Id == placa.VendaId && v.DoCanalDigital, ct);
            venda.Cancelar(Domain.Vendas.MotivoDoCancelamento.Devolucao, "Devolução do pedido digital", agora);
            placa.DesfazerVenda();
            placa.Baixar(MotivoDaBaixa.Devolucao, agora);
        }

        b.Lancamentos.Add(receita.Estorno("devolução do pedido digital", agora));
        b.AvisarAoVivo("estoque", revendedorId, operadora: true);
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
