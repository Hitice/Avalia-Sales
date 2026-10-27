using AvaliaSales.Domain.Financeiro;
using AvaliaSales.Domain.Pagamentos;
using AvaliaSales.Domain.Pedidos;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Financeiro;

/// <summary>Uma linha do resultado, em positivo: receita soma, custo subtrai. O consolidado é a soma das três colunas.</summary>
public sealed record LinhaDoResultado(string Chave, string Rotulo, TipoDaLinhaDoResultado Tipo, long RevendaCents, long OperadoraCents, long EliminacoesCents, long ConsolidadoCents);

public enum TipoDaLinhaDoResultado
{
    Receita = 1,
    Custo = 2,
    Total = 3,
    Resultado = 4,
}

/// <summary>Cada transação interna lida de dois lados, de fontes diferentes: a diferença é zero quando o livro e o razão estão completos.</summary>
public sealed record LinhaDeConciliacao(string Chave, string Rotulo, long NaRevendaCents, long NaOperadoraCents, long DiferencaCents);

/// <summary>Pedidos pagos e ainda não ativados no fim do mês: adiantamento de cliente, e não receita.</summary>
public sealed record AindaNaoReconhecido(long TotalCents, int Pedidos, long DaRevendaDoGrupoCents);

public sealed record PendenciasDoGrupo(int LotesSemCusto, int PlacasVendidasSemCusto);

public sealed record ResultadoDoGrupoDto(
    string Competencia,
    bool Fechado,
    DateTimeOffset? FechadoEm,
    int RevendasDoGrupo,
    IReadOnlyList<LinhaDoResultado> Linhas,
    IReadOnlyList<LinhaDeConciliacao> Conciliacao,
    long DiferencaDaConciliacaoCents,
    AindaNaoReconhecido AindaNaoReconhecido,
    PendenciasDoGrupo Pendencias,
    long RetiradasDeSocioCents);

public sealed record ReceitaDaRevenda(Guid RevendedorId, string Revendedor, bool DoGrupo, long ParteDaAvaliaCents, long PedidosDePlacasCents, long TotalCents);

/// <summary>
/// O resultado consolidado do grupo (a Avalia empresa: revenda do grupo mais operadora), por mês.
/// Consolidação com eliminação das transações internas (CPC 36 / IFRS 10), receita no controle (CPC 47 /
/// IFRS 15) e informação por segmento (CPC 22 / IFRS 8): as colunas são a revenda do grupo, a operadora,
/// as eliminações e o consolidado. Revenda fora do grupo entra só pelo que paga à operadora (ADR 0025).
/// </summary>
public sealed class ResultadoDoGrupo(IBancoDeDados banco, IEscopoDeRevenda escopo, FechamentosDaOperadora fechamentos, TimeProvider relogio)
{
    public async Task<ResultadoDoGrupoDto> DoMes(string? competencia, CancellationToken ct)
    {
        var mes = string.IsNullOrWhiteSpace(competencia) ? fechamentos.MesCorrente() : Competencias.Conferir(competencia);
        var (inicio, fim) = (Lancamento.InicioDe(mes), Lancamento.InicioDe(Competencias.Somar(mes, 1)));
        var doGrupo = await banco.Revendedores.AsNoTracking().Where(r => r.DoGrupoDaOperadora).Select(r => r.Id).ToListAsync(ct);

        // O livro da operadora no mês, de qualquer revenda, separado pelo que veio do grupo.
        var livro = await (
            from l in banco.LancamentosDaOperadora.AsNoTracking()
            where l.Competencia == mes
            join r in banco.Revendedores on l.RevendedorOrigemId equals r.Id into revendedores
            from r in revendedores.DefaultIfEmpty()
            select new { l.Tipo, l.ValorCents, DoGrupo = r != null && r.DoGrupoDaOperadora })
            .GroupBy(x => new { x.Tipo, x.DoGrupo })
            .Select(g => new { g.Key.Tipo, g.Key.DoGrupo, Total = g.Sum(x => x.ValorCents) })
            .ToListAsync(ct);
        long Livro(TipoDeLancamentoDaOperadora tipo, bool? soDoGrupo = null) =>
            livro.Where(x => x.Tipo == tipo && (soDoGrupo is null || x.DoGrupo == soDoGrupo)).Sum(x => x.Total);

        // A taxa da plataforma saiu em 27/10/2026; a linha antiga dela soma na parte da Avalia, dos dois lados da eliminação.
        long ParteNoLivro(bool? soDoGrupo = null) =>
            Livro(TipoDeLancamentoDaOperadora.ParteDaAvaliaNosServicos, soDoGrupo) + Livro(TipoDeLancamentoDaOperadora.TaxaDaPlataforma, soDoGrupo);

        // Os lotes que passaram a ser do cliente no mês, a custo real.
        var ativados = await (
            from c in banco.CustosDeLote.AsNoTracking()
            join l in banco.Lotes.AsNoTracking() on c.LoteId equals l.Id
            where l.AtivadoEm >= inicio && l.AtivadoEm < fim
            select new { l.RevendedorId, c.GraficaCents, c.AcrilicoCents }).ToListAsync(ct);
        var custoAtivado = ativados.Sum(x => x.GraficaCents + x.AcrilicoCents);
        var custoAtivadoDoGrupo = ativados.Where(x => x.RevendedorId is { } r && doGrupo.Contains(r)).Sum(x => x.GraficaCents + x.AcrilicoCents);

        // O que cada revenda do grupo mostra, só em agregado: o escopo recusa quem não foi marcada.
        var leituras = new List<LeituraDaRevenda>();
        foreach (var id in doGrupo)
        {
            leituras.Add(await escopo.Ler(id, b => LerRevenda(b, mes, ct), ct));
        }

        var custosPorPlaca = await CustosPorPlaca(leituras, ct);
        var real = ApurarCustoReal(leituras, custosPorPlaca, mes);

        var pedidosDoGrupo = await banco.Pedidos.AsNoTracking()
            .Where(p => p.AtivadoEm >= inicio && p.AtivadoEm < fim && p.ValorCents > 0 && doGrupo.Contains(p.RevendedorId)).SumAsync(p => (long?)p.ValorCents, ct) ?? 0;
        var naoReconhecidos = await banco.Pedidos.AsNoTracking()
            .Where(p => p.PagoEm != null && p.PagoEm < fim && (p.AtivadoEm == null || p.AtivadoEm >= fim) && p.Situacao != SituacaoDoPedido.Cancelado && p.ValorCents > 0)
            .Select(p => new { p.RevendedorId, p.ValorCents }).ToListAsync(ct);

        var fechamento = await banco.FechamentosDaOperadora.AsNoTracking().Where(f => f.Competencia == mes && f.ReabertoEm == null).Select(f => (DateTimeOffset?)f.FechadoEm).SingleOrDefaultAsync(ct);
        var despesasDaOperadora = -Livro(TipoDeLancamentoDaOperadora.Despesa);

        long Soma(Func<DreDoMes, long> campo) => leituras.Sum(l => campo(l.Dre));
        var parteDaAvaliaPaga = Soma(d => d.ParteDaAvaliaCents);

        var receitas = new[]
        {
            Linha("receita-das-vendas", "Receita das vendas ao cliente final", TipoDaLinhaDoResultado.Receita, Soma(d => d.ReceitaReconhecidaCents), 0, 0),
            Linha("parte-da-avalia", "Parte da Avalia nos serviços", TipoDaLinhaDoResultado.Receita, 0, ParteNoLivro(), -ParteNoLivro(true)),
            Linha("pedidos-de-placas", "Pedidos de placas ativados", TipoDaLinhaDoResultado.Receita, 0, Livro(TipoDeLancamentoDaOperadora.PedidoDePlacas),
                -Livro(TipoDeLancamentoDaOperadora.PedidoDePlacas, true)),
        };
        var custos = new[]
        {
            Linha("parte-da-avalia-paga", "Parte da Avalia paga pela revenda", TipoDaLinhaDoResultado.Custo, parteDaAvaliaPaga, 0, -parteDaAvaliaPaga),
            Linha("comissoes", "Comissões", TipoDaLinhaDoResultado.Custo, Soma(d => d.ComissoesCents), 0, 0),
            Linha("placas-pelo-preco-da-operadora", "Placas pelo preço pago à operadora", TipoDaLinhaDoResultado.Custo,
                Soma(d => d.CustoDasPlacasCents + d.PerdasCents), 0, -Soma(d => d.CustoDasPlacasCents + d.PerdasCents)),
            Linha("custo-de-producao", "Custo real de produção das placas", TipoDaLinhaDoResultado.Custo, 0, custoAtivado, -custoAtivadoDoGrupo + real.CustoCents),
            Linha("despesas-da-revenda", "Despesas da revenda", TipoDaLinhaDoResultado.Custo, Soma(d => d.DespesasCents), 0, 0),
            Linha("despesas-da-operadora", "Despesas da operadora", TipoDaLinhaDoResultado.Custo, 0, despesasDaOperadora, 0),
        };
        var totalDaReceita = Total("total-da-receita", "Receita", TipoDaLinhaDoResultado.Total, receitas);
        var totalDosCustos = Total("total-dos-custos", "Custos e despesas", TipoDaLinhaDoResultado.Total, custos);
        var resultado = Linha("resultado", "Resultado", TipoDaLinhaDoResultado.Resultado,
            totalDaReceita.RevendaCents - totalDosCustos.RevendaCents, totalDaReceita.OperadoraCents - totalDosCustos.OperadoraCents,
            totalDaReceita.EliminacoesCents - totalDosCustos.EliminacoesCents);

        var conciliacao = new[]
        {
            Conciliar("pedido-de-placas", "Pedido de placas: compra da revenda do grupo e receita da operadora", pedidosDoGrupo, Livro(TipoDeLancamentoDaOperadora.PedidoDePlacas, true)),
            Conciliar("parte-da-avalia", "Parte da Avalia: paga pela revenda do grupo e recebida pela operadora", parteDaAvaliaPaga, ParteNoLivro(true)),
        };

        var lotesSemCusto = await banco.Lotes.AsNoTracking().CountAsync(l => l.PedidoId != null && l.ArquivadoEm == null && !banco.CustosDeLote.Any(c => c.LoteId == l.Id), ct);
        return new ResultadoDoGrupoDto(mes, fechamento is not null, fechamento, doGrupo.Count,
            [.. receitas, totalDaReceita, .. custos, totalDosCustos, resultado], conciliacao, conciliacao.Sum(c => c.DiferencaCents),
            new AindaNaoReconhecido(naoReconhecidos.Sum(p => p.ValorCents), naoReconhecidos.Count, naoReconhecidos.Where(p => doGrupo.Contains(p.RevendedorId)).Sum(p => p.ValorCents)),
            new PendenciasDoGrupo(lotesSemCusto, real.PlacasSemCusto), Soma(d => d.RetiradasCents));
    }

    /// <summary>
    /// O que as revendas pagam à operadora no mês, uma linha por revenda, para ver de quem vem a receita. Mostra
    /// só a receita da operadora; o DRE de revenda fora do grupo nunca é lido, e o da revenda do grupo só entra na soma.
    /// </summary>
    public async Task<Pagina<ReceitaDaRevenda>> PorRevenda(string? competencia, string? busca, PedidoDePagina pedido, CancellationToken ct)
    {
        var mes = string.IsNullOrWhiteSpace(competencia) ? fechamentos.MesCorrente() : Competencias.Conferir(competencia);
        var somas =
            from l in banco.LancamentosDaOperadora.AsNoTracking()
            where l.Competencia == mes && l.RevendedorOrigemId != null
            group l by l.RevendedorOrigemId into g
            select new
            {
                Origem = g.Key,
                // A taxa da plataforma (só histórico) soma na parte da Avalia.
                Parte = g.Where(l => l.Tipo == TipoDeLancamentoDaOperadora.ParteDaAvaliaNosServicos || l.Tipo == TipoDeLancamentoDaOperadora.TaxaDaPlataforma).Sum(l => l.ValorCents),
                Pedidos = g.Where(l => l.Tipo == TipoDeLancamentoDaOperadora.PedidoDePlacas).Sum(l => l.ValorCents),
            };
        var consulta =
            from x in somas
            join r in banco.Revendedores.AsNoTracking() on x.Origem equals r.Id
            select new { r.Id, r.Nome, r.DoGrupoDaOperadora, x.Parte, x.Pedidos };
        var termo = Paginacao.Termo(busca);
        if (termo.Length > 0)
        {
            consulta = consulta.Where(x => Busca.Normalizar(x.Nome).Contains(Busca.Normalizar(termo)));
        }

        return (await consulta.OrderBy(x => x.Nome).ThenBy(x => x.Id).PaginarAsync(pedido, ct))
            .Mapear(x => new ReceitaDaRevenda(x.Id, x.Nome, x.DoGrupoDaOperadora, x.Parte, x.Pedidos, x.Parte + x.Pedidos));
    }

    private static LinhaDoResultado Linha(string chave, string rotulo, TipoDaLinhaDoResultado tipo, long revenda, long operadora, long eliminacoes) =>
        new(chave, rotulo, tipo, revenda, operadora, eliminacoes, revenda + operadora + eliminacoes);

    private static LinhaDoResultado Total(string chave, string rotulo, TipoDaLinhaDoResultado tipo, IReadOnlyList<LinhaDoResultado> linhas) =>
        Linha(chave, rotulo, tipo, linhas.Sum(l => l.RevendaCents), linhas.Sum(l => l.OperadoraCents), linhas.Sum(l => l.EliminacoesCents));

    private static LinhaDeConciliacao Conciliar(string chave, string rotulo, long naRevenda, long naOperadora) =>
        new(chave, rotulo, naRevenda, naOperadora, naRevenda - naOperadora);

    // ---- A revenda do grupo, lida na sessão dela ----

    private sealed record PlacaDaReceita(Guid Id, long ValorCents, string Competencia, int Meses, Guid? EstornaId, Guid LoteId, bool ComCusto);

    private sealed record PlacaSemReceita(long ValorCents, Guid LoteId);

    private sealed record LeituraDaRevenda(DreDoMes Dre, IReadOnlyList<PlacaDaReceita> Receitas, IReadOnlyList<PlacaSemReceita> SemReceita);

    private async Task<LeituraDaRevenda> LerRevenda(IBancoDeDados b, string mes, CancellationToken ct)
    {
        var dre = await new Dre(b, relogio).DoMes(mes, ct);

        var receitas = (await (
            from l in b.Lancamentos.AsNoTracking()
            where l.Tipo == TipoDeLancamento.Receita
            join v in b.Vendas on l.VendaId equals v.Id
            where v.PlacaId != null
            join c in b.Cobrancas on l.OrigemId equals c.Id into cobrancas
            from c in cobrancas.DefaultIfEmpty()
            join p in b.Placas on v.PlacaId equals p.Id
            select new
            {
                l.Id, l.ValorCents, l.Competencia, l.EstornaId, p.LoteId,
                DaVenda = c != null && c.Origem == OrigemDaCobranca.Venda,
                DoHistorico = v.DoSistemaAnterior && c == null,
            }).ToListAsync(ct))
            .Where(r => string.CompareOrdinal(r.Competencia, mes) <= 0)
            .Select(r => new PlacaDaReceita(r.Id, r.ValorCents, r.Competencia, r.DoHistorico ? 1 : Reconhecimento.MesesDaPlaca, r.EstornaId, r.LoteId, r.DaVenda || r.DoHistorico))
            .ToList();
        var semReceita = await (
            from l in b.Lancamentos.AsNoTracking()
            where (l.Tipo == TipoDeLancamento.Perda || l.Tipo == TipoDeLancamento.CustoDaPlaca) && l.Competencia == mes && l.PlacaId != null
            join p in b.Placas on l.PlacaId equals p.Id
            select new PlacaSemReceita(l.ValorCents, p.LoteId)).ToListAsync(ct);
        return new LeituraDaRevenda(dre, receitas, semReceita);
    }

    /// <summary>O custo por placa de cada lote com custo informado. Lote sem custo não entra: a placa dele conta como pendência.</summary>
    private async Task<Dictionary<Guid, long>> CustosPorPlaca(List<LeituraDaRevenda> leituras, CancellationToken ct)
    {
        var lotes = leituras.SelectMany(l => l.Receitas.Select(r => r.LoteId).Concat(l.SemReceita.Select(s => s.LoteId))).Distinct().ToList();
        return (await banco.CustosDeLote.AsNoTracking().Where(c => lotes.Contains(c.LoteId)).ToListAsync(ct)).ToDictionary(c => c.LoteId, c => c.PorPlacaCents);
    }

    /// <summary>
    /// O custo real das placas do grupo no mês: a vendida acompanha a receita da venda (as mesmas doze partes
    /// da DRE, com o custo da gráfica e do acrílico no lugar do preço pago à operadora), e a perdida, quebrada
    /// ou dada em cortesia entra no mês em que saiu.
    /// </summary>
    private static (long CustoCents, int PlacasSemCusto) ApurarCustoReal(List<LeituraDaRevenda> leituras, Dictionary<Guid, long> custoPorPlaca, string mes)
    {
        long custo = 0;
        var semCusto = 0;
        foreach (var leitura in leituras)
        {
            var apropriadas = leitura.Receitas.Where(r => r.ComCusto && custoPorPlaca.GetValueOrDefault(r.LoteId) > 0)
                .Select(r => new LinhaApropriada(r.Id, -Math.Sign(r.ValorCents) * custoPorPlaca[r.LoteId], r.Competencia, r.Meses, r.EstornaId)).ToList();
            custo += -Reconhecimento.NoMes(apropriadas, mes);
            custo += leitura.SemReceita.Where(s => custoPorPlaca.GetValueOrDefault(s.LoteId) > 0).Sum(s => -Math.Sign(s.ValorCents) * custoPorPlaca[s.LoteId]);
            semCusto += leitura.Receitas.Count(r => r.ComCusto && r.EstornaId is null && r.Competencia == mes && custoPorPlaca.GetValueOrDefault(r.LoteId) <= 0);
        }

        return (custo, semCusto);
    }
}
