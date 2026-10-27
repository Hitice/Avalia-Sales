using AvaliaSales.Domain;
using AvaliaSales.Domain.Financeiro;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Financeiro;

/// <summary>
/// O fechamento do mês do livro da operadora. Fechado, o mês não recebe linha: a despesa nova com
/// data nele é recusada, e a correção de linha dele vai ao mês corrente. O banco repete a trava
/// (gatilho em lancamentos_da_operadora). Quem fecha fica na trilha da operadora, pela rota.
/// </summary>
public sealed class FechamentosDaOperadora(IBancoDeDados banco, IUsuarioAtual usuario, TimeProvider relogio)
{
    public string MesCorrente() => Lancamento.CompetenciaDe(relogio.GetUtcNow());

    public Task<bool> Fechado(string competencia, CancellationToken ct) =>
        banco.FechamentosDaOperadora.AnyAsync(f => f.Competencia == competencia && f.ReabertoEm == null, ct);

    /// <summary>Onde entra a correção de uma linha do mês: nele, se aberto; senão, no mês corrente.</summary>
    public async Task<string> DestinoDaCorrecao(string competencia, CancellationToken ct) =>
        await Fechado(competencia, ct) ? MesCorrente() : competencia;

    public async Task ExigirAberto(string competencia, CancellationToken ct)
    {
        if (await Fechado(competencia, ct))
        {
            throw new RegraDeNegocioException($"Mês {Fechamentos.Nome(competencia)} fechado. Use outra data ou reabra o mês.");
        }
    }

    /// <summary>Os meses com lançamento, mais o corrente e os fechados, do mais recente para trás.</summary>
    public async Task<IReadOnlyList<MesDoFinanceiro>> Meses(CancellationToken ct)
    {
        var comLinha = await banco.LancamentosDaOperadora.AsNoTracking().Select(l => l.Competencia).Distinct().ToListAsync(ct);
        var fechados = await banco.FechamentosDaOperadora.AsNoTracking().Where(f => f.ReabertoEm == null).ToDictionaryAsync(f => f.Competencia, f => f.FechadoEm, ct);
        return comLinha.Append(MesCorrente()).Concat(fechados.Keys).Distinct().OrderDescending()
            .Select(m => new MesDoFinanceiro(m, fechados.ContainsKey(m), fechados.TryGetValue(m, out var em) ? em : null)).ToList();
    }

    public async Task Fechar(string competencia, CancellationToken ct)
    {
        var fechamento = new FechamentoDaOperadora(competencia, usuario.UsuarioId, relogio.GetUtcNow());
        if (await Fechado(fechamento.Competencia, ct))
        {
            throw new RegraDeNegocioException($"Mês {Fechamentos.Nome(fechamento.Competencia)} já fechado.");
        }

        banco.FechamentosDaOperadora.Add(fechamento);
        await banco.SaveChangesAsync(ct);
    }

    public async Task Reabrir(string competencia, ReabrirMes pedido, CancellationToken ct)
    {
        var mes = Competencias.Conferir(competencia);
        var fechamento = await banco.FechamentosDaOperadora.SingleOrDefaultAsync(f => f.Competencia == mes && f.ReabertoEm == null, ct)
            ?? throw new RegraDeNegocioException($"Mês {Fechamentos.Nome(mes)} não fechado.");
        fechamento.Reabrir(pedido.Motivo, usuario.UsuarioId, relogio.GetUtcNow());
        await banco.SaveChangesAsync(ct);
    }
}
