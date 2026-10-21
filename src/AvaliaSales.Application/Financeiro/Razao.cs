using AvaliaSales.Domain.Financeiro;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Financeiro;

public sealed record LancamentoDto(Guid Id, TipoDeLancamento Tipo, long ValorCents, string Descricao, DateTimeOffset OcorridoEm, string Competencia, bool Estorno);

public sealed record ResumoDoMes(string Competencia, long ReceitaCents, long TaxaAvaliaCents, long ComissaoCents, long LiquidoCents, IReadOnlyList<LancamentoDto> Lancamentos);

/// <summary>
/// O razão do revendedor por mês. Líquido é o que fica com ele depois da taxa
/// da Avalia e da comissão; a taxa do Pagar.me sai no extrato do Pagar.me.
/// </summary>
public sealed class Razao(IBancoDeDados banco, TimeProvider relogio)
{
    public async Task<ResumoDoMes> DoMes(string? competencia, CancellationToken ct)
    {
        var mes = string.IsNullOrWhiteSpace(competencia) ? Lancamento.CompetenciaDe(relogio.GetUtcNow()) : competencia.Trim();
        var linhas = await banco.Lancamentos.AsNoTracking().Where(l => l.Competencia == mes)
            .OrderByDescending(l => l.OcorridoEm).ToListAsync(ct);
        long Soma(TipoDeLancamento tipo) => linhas.Where(l => l.Tipo == tipo).Sum(l => l.ValorCents);
        var receita = Soma(TipoDeLancamento.Receita);
        var taxa = -Soma(TipoDeLancamento.TaxaAvalia);
        var comissao = -Soma(TipoDeLancamento.ComissaoDevida);
        return new ResumoDoMes(mes, receita, taxa, comissao, receita - taxa - comissao,
            linhas.Select(l => new LancamentoDto(l.Id, l.Tipo, l.ValorCents, l.Descricao, l.OcorridoEm, l.Competencia, l.EstornaId is not null)).ToList());
    }

    /// <summary>Os meses com lançamento, do mais recente para trás.</summary>
    public async Task<IReadOnlyList<string>> Meses(CancellationToken ct) =>
        await banco.Lancamentos.AsNoTracking().Select(l => l.Competencia).Distinct().OrderByDescending(c => c).ToListAsync(ct);
}
