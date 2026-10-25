using AvaliaSales.Domain.Financeiro;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Financeiro;

public sealed record LancamentoDto(Guid Id, TipoDeLancamento Tipo, long ValorCents, string Descricao, DateTimeOffset OcorridoEm, string Competencia, bool Estorno);

public sealed record ResumoDoMes(string Competencia, long ReceitaCents, long TaxaAvaliaCents, long ComissaoCents, long LiquidoCents, Pagina<LancamentoDto> Lancamentos);

/// <summary>
/// O razão do revendedor por mês. Líquido é o que fica com ele depois da taxa
/// da Avalia e da comissão; a taxa do Pagar.me sai no extrato do Pagar.me.
/// </summary>
public sealed class Razao(IBancoDeDados banco, TimeProvider relogio)
{
    /// <summary>
    /// Os totais são do mês inteiro; os lançamentos vêm em páginas, do mais novo ao mais antigo. A busca
    /// vale para a descrição e só filtra a lista, e não os totais.
    /// </summary>
    public async Task<ResumoDoMes> DoMes(string? competencia, string? busca, PedidoDePagina pedido, CancellationToken ct)
    {
        var mes = string.IsNullOrWhiteSpace(competencia) ? Lancamento.CompetenciaDe(relogio.GetUtcNow()) : competencia.Trim();
        var doMes = banco.Lancamentos.AsNoTracking().Where(l => l.Competencia == mes);
        var somas = await doMes.GroupBy(l => l.Tipo).Select(g => new { Tipo = g.Key, Total = g.Sum(l => l.ValorCents) }).ToListAsync(ct);
        long Soma(TipoDeLancamento tipo) => somas.Where(l => l.Tipo == tipo).Sum(l => l.Total);
        var receita = Soma(TipoDeLancamento.Receita);
        var taxa = -Soma(TipoDeLancamento.TaxaAvalia);
        var comissao = -Soma(TipoDeLancamento.ComissaoDevida);

        var termo = Paginacao.Termo(busca);
        if (termo.Length > 0)
        {
            // Vira lower(...) LIKE no Postgres: a cultura do .NET não entra na consulta.
#pragma warning disable CA1304, CA1311, CA1862
            doMes = doMes.Where(l => l.Descricao.ToLower().Contains(termo));
#pragma warning restore CA1304, CA1311, CA1862
        }

        var pagina = await doMes.OrderByDescending(l => l.OcorridoEm).ThenByDescending(l => l.Id).PaginarAsync(pedido, ct);
        return new ResumoDoMes(mes, receita, taxa, comissao, receita - taxa - comissao,
            pagina.Mapear(l => new LancamentoDto(l.Id, l.Tipo, l.ValorCents, l.Descricao, l.OcorridoEm, l.Competencia, l.EstornaId is not null)));
    }

    /// <summary>Os meses com lançamento, do mais recente para trás.</summary>
    public async Task<IReadOnlyList<string>> Meses(CancellationToken ct) =>
        await banco.Lancamentos.AsNoTracking().Select(l => l.Competencia).Distinct().OrderByDescending(c => c).ToListAsync(ct);
}
