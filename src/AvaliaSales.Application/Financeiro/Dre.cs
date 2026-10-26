using AvaliaSales.Domain.Financeiro;
using AvaliaSales.Domain.Pagamentos;
using AvaliaSales.Domain.Vendas;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Financeiro;

public sealed record DespesaDaCategoria(CategoriaDeDespesa Categoria, long ValorCents);

public sealed record RetiradaDaNatureza(NaturezaDaRetirada Natureza, long ValorCents);

/// <summary>
/// O resultado do mês. Vendido é o valor das vendas registradas no mês que
/// seguem de pé; recebido é o caixa (pagamentos menos estornos); receita
/// reconhecida é a parte do mês. Taxa, comissão, custo, perda e despesa vêm
/// em positivo e saem da receita reconhecida; retirada de sócio fica abaixo
/// do resultado, porque não é despesa.
/// </summary>
public sealed record DreDoMes(
    string Competencia,
    bool Fechado,
    DateTimeOffset? FechadoEm,
    int Vendas,
    long VendidoCents,
    long RecebidoCents,
    long ReceitaReconhecidaCents,
    long AReconhecerCents,
    long TaxaCents,
    long ComissoesCents,
    long CustoDasPlacasCents,
    long PerdasCents,
    long DespesasCents,
    IReadOnlyList<DespesaDaCategoria> DespesasPorCategoria,
    long ResultadoCents,
    long RetiradasCents,
    IReadOnlyList<RetiradaDaNatureza> RetiradasPorNatureza);

public sealed record MesDoHistorico(string Competencia, long RecebidoCents, long ResultadoCents);

/// <summary>
/// A DRE gerencial do dono, toda lida do razão e das vendas. A receita da
/// placa (venda e renovação) entra em 12 partes a partir do mês do pagamento,
/// e o custo da placa vendida acompanha a receita da venda; o serviço entra
/// inteiro no mês. Comissão e taxa entram no mês do pagamento (CPC 47, item
/// 94: contrato de até um ano). Como o razão de mês fechado não muda, a DRE
/// dele também não.
/// </summary>
public sealed class Dre(IBancoDeDados banco, TimeProvider relogio)
{
    private string MesCorrente() => Lancamento.CompetenciaDe(relogio.GetUtcNow());

    public const int MesesDoHistorico = 6;

    public async Task<DreDoMes> DoMes(string? competencia, CancellationToken ct)
    {
        var mes = string.IsNullOrWhiteSpace(competencia) ? MesCorrente() : Competencias.Conferir(competencia);
        var (linhas, receitas, custos) = await Carregar(mes, ct);
        var (inicio, fim) = (Lancamento.InicioDe(mes), Lancamento.InicioDe(Competencias.Somar(mes, 1)));
        var vendidas = await banco.Vendas.AsNoTracking()
            .Where(v => v.VendidaEm >= inicio && v.VendidaEm < fim && v.Situacao == SituacaoDaVenda.Registrada)
            .GroupBy(_ => 1).Select(g => new { Quantas = g.Count(), Valor = g.Sum(v => v.ValorCents) }).SingleOrDefaultAsync(ct);
        var fechamento = await banco.Fechamentos.AsNoTracking().Where(f => f.Competencia == mes && f.ReabertoEm == null).Select(f => (DateTimeOffset?)f.FechadoEm).SingleOrDefaultAsync(ct);

        var doMes = linhas.Where(l => l.Competencia == mes).ToList();
        long Saida(TipoDeLancamento tipo) => -doMes.Where(l => l.Tipo == tipo).Sum(l => l.ValorCents);
        var porCategoria = doMes.Where(l => l.Tipo == TipoDeLancamento.Despesa && l.Categoria is not null)
            .GroupBy(l => Enum.Parse<CategoriaDeDespesa>(l.Categoria!)).Select(g => new DespesaDaCategoria(g.Key, -g.Sum(l => l.ValorCents)))
            .Where(d => d.ValorCents != 0).OrderByDescending(d => d.ValorCents).ToList();
        var porNatureza = doMes.Where(l => l.Tipo == TipoDeLancamento.RetiradaDeSocio && l.Categoria is not null)
            .GroupBy(l => Enum.Parse<NaturezaDaRetirada>(l.Categoria!)).Select(g => new RetiradaDaNatureza(g.Key, -g.Sum(l => l.ValorCents)))
            .Where(r => r.ValorCents != 0).OrderBy(r => r.Natureza).ToList();

        var reconhecida = Reconhecimento.NoMes(receitas, mes);
        var custo = -Reconhecimento.NoMes(custos, mes) + Saida(TipoDeLancamento.CustoDaPlaca);
        var (taxa, comissoes, perdas, despesas) = (Saida(TipoDeLancamento.TaxaAvalia), Saida(TipoDeLancamento.ComissaoDevida), Saida(TipoDeLancamento.Perda), Saida(TipoDeLancamento.Despesa));
        return new DreDoMes(mes, fechamento is not null, fechamento, vendidas?.Quantas ?? 0, vendidas?.Valor ?? 0,
            doMes.Where(l => l.Tipo == TipoDeLancamento.Receita).Sum(l => l.ValorCents),
            reconhecida, Reconhecimento.AReconhecer(receitas, mes),
            taxa, comissoes, custo, perdas, despesas, porCategoria,
            reconhecida - taxa - comissoes - custo - perdas - despesas,
            Saida(TipoDeLancamento.RetiradaDeSocio), porNatureza);
    }

    /// <summary>Recebido e resultado dos últimos meses até <paramref name="ate"/>, do mais antigo ao mais novo, para o gráfico.</summary>
    public async Task<IReadOnlyList<MesDoHistorico>> Historico(string? ate, CancellationToken ct)
    {
        var ultimo = string.IsNullOrWhiteSpace(ate) ? MesCorrente() : Competencias.Conferir(ate);
        var (linhas, receitas, custos) = await Carregar(ultimo, ct);
        return Enumerable.Range(0, MesesDoHistorico).Select(i => Competencias.Somar(ultimo, i - MesesDoHistorico + 1)).Select(mes =>
        {
            var doMes = linhas.Where(l => l.Competencia == mes).ToList();
            long Soma(TipoDeLancamento tipo) => doMes.Where(l => l.Tipo == tipo).Sum(l => l.ValorCents);
            // As saídas já vêm negativas do razão: somar é subtrair.
            var resultado = Reconhecimento.NoMes(receitas, mes) + Reconhecimento.NoMes(custos, mes) + Soma(TipoDeLancamento.CustoDaPlaca)
                + Soma(TipoDeLancamento.TaxaAvalia) + Soma(TipoDeLancamento.ComissaoDevida) + Soma(TipoDeLancamento.Perda) + Soma(TipoDeLancamento.Despesa);
            return new MesDoHistorico(mes, Soma(TipoDeLancamento.Receita), resultado);
        }).ToList();
    }

    private sealed record Linha(TipoDeLancamento Tipo, long ValorCents, string Competencia, string? Categoria);

    /// <summary>
    /// As linhas até o mês. A receita leva junto a venda e a cobrança: placa
    /// apropria em 12 meses, e a receita da venda da placa (não a da renovação)
    /// traz o custo dela, com o mesmo sinal da receita e o mesmo estorno.
    /// </summary>
    private async Task<(List<Linha> Linhas, List<LinhaApropriada> Receitas, List<LinhaApropriada> Custos)> Carregar(string ate, CancellationToken ct)
    {
        // O mês é texto de tamanho fixo (2026-10): a ordem do texto é a do tempo, e o corte é feito aqui.
        var linhas = (await banco.Lancamentos.AsNoTracking()
            .Select(l => new Linha(l.Tipo, l.ValorCents, l.Competencia, l.Categoria)).ToListAsync(ct))
            .Where(l => string.CompareOrdinal(l.Competencia, ate) <= 0).ToList();
        var receitas = await (
            from l in banco.Lancamentos.AsNoTracking()
            where l.Tipo == TipoDeLancamento.Receita
            join v in banco.Vendas on l.VendaId equals v.Id into vendas
            from v in vendas.DefaultIfEmpty()
            join c in banco.Cobrancas on l.OrigemId equals c.Id into cobrancas
            from c in cobrancas.DefaultIfEmpty()
            select new
            {
                l.Id, l.ValorCents, l.Competencia, l.EstornaId,
                DaPlaca = v != null && v.PlacaId != null,
                Custo = v == null ? 0 : v.CustoCents,
                DaVenda = c != null && c.Origem == OrigemDaCobranca.Venda,
                // Histórico trazido do sistema anterior: a linha não tem cobrança. A renovação paga aqui, sim.
                DoHistorico = v != null && v.DoSistemaAnterior && c == null,
            }).ToListAsync(ct);
        receitas = receitas.Where(r => string.CompareOrdinal(r.Competencia, ate) <= 0).ToList();
        // O histórico fica inteiro no mês da venda, como era reconhecido lá: nenhuma parte chega aos meses da plataforma.
        int Meses(bool daPlaca, bool doHistorico) => daPlaca && !doHistorico ? Reconhecimento.MesesDaPlaca : 1;
        return (linhas,
            receitas.Select(r => new LinhaApropriada(r.Id, r.ValorCents, r.Competencia, Meses(r.DaPlaca, r.DoHistorico), r.EstornaId)).ToList(),
            receitas.Where(r => r.DaPlaca && (r.DaVenda || r.DoHistorico) && r.Custo > 0)
                .Select(r => new LinhaApropriada(r.Id, -Math.Sign(r.ValorCents) * r.Custo, r.Competencia, Meses(r.DaPlaca, r.DoHistorico), r.EstornaId)).ToList());
    }
}
