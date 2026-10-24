using AvaliaSales.Domain;
using AvaliaSales.Domain.Auditoria;
using AvaliaSales.Domain.Financeiro;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Financeiro;

public sealed record MesDoFinanceiro(string Competencia, bool Fechado, DateTimeOffset? FechadoEm);

public sealed record FecharMes(string Competencia);

public sealed record ReabrirMes(string Motivo);

/// <summary>
/// O fechamento do mês pelo dono. Fechado, o mês não recebe linha: a despesa
/// nova com data nele é recusada, e a correção de linha dele vai ao mês
/// corrente. O banco repete a trava (gatilho em lancamentos).
/// </summary>
public sealed class Fechamentos(IBancoDeDados banco, IUsuarioAtual usuario, Auditoria.Auditoria auditoria, TimeProvider relogio)
{
    public string MesCorrente() => Lancamento.CompetenciaDe(relogio.GetUtcNow());

    public Task<bool> Fechado(string competencia, CancellationToken ct) =>
        banco.Fechamentos.AnyAsync(f => f.Competencia == competencia && f.ReabertoEm == null, ct);

    /// <summary>Onde entra a correção de uma linha do mês: nele, se aberto; senão, no mês corrente.</summary>
    public async Task<string> DestinoDaCorrecao(string competencia, CancellationToken ct) =>
        await Fechado(competencia, ct) ? MesCorrente() : competencia;

    public async Task ExigirAberto(string competencia, CancellationToken ct)
    {
        if (await Fechado(competencia, ct))
        {
            throw new RegraDeNegocioException($"O mês {Nome(competencia)} está fechado. Use uma data de mês aberto ou reabra o mês.");
        }
    }

    /// <summary>Os meses com lançamento, mais o corrente e os fechados, do mais recente para trás.</summary>
    public async Task<IReadOnlyList<MesDoFinanceiro>> Meses(CancellationToken ct)
    {
        var comLinha = await banco.Lancamentos.AsNoTracking().Select(l => l.Competencia).Distinct().ToListAsync(ct);
        var fechados = await banco.Fechamentos.AsNoTracking().Where(f => f.ReabertoEm == null).ToDictionaryAsync(f => f.Competencia, f => f.FechadoEm, ct);
        return comLinha.Append(MesCorrente()).Concat(fechados.Keys).Distinct().OrderDescending()
            .Select(m => new MesDoFinanceiro(m, fechados.ContainsKey(m), fechados.TryGetValue(m, out var em) ? em : null)).ToList();
    }

    public async Task Fechar(string competencia, CancellationToken ct)
    {
        var fechamento = new FechamentoDoMes(competencia, usuario.UsuarioId, relogio.GetUtcNow());
        if (await Fechado(fechamento.Competencia, ct))
        {
            throw new RegraDeNegocioException($"O mês {Nome(fechamento.Competencia)} já está fechado.");
        }

        banco.Fechamentos.Add(fechamento);
        auditoria.Registrar(AcaoAuditada.MesFechado, $"Mês {Nome(fechamento.Competencia)}", fechamento.Competencia);
        await banco.SaveChangesAsync(ct);
    }

    public async Task Reabrir(string competencia, ReabrirMes pedido, CancellationToken ct)
    {
        var mes = Competencias.Conferir(competencia);
        var fechamento = await banco.Fechamentos.SingleOrDefaultAsync(f => f.Competencia == mes && f.ReabertoEm == null, ct)
            ?? throw new RegraDeNegocioException($"O mês {Nome(mes)} não está fechado.");
        fechamento.Reabrir(pedido.Motivo, usuario.UsuarioId, relogio.GetUtcNow());
        auditoria.Registrar(AcaoAuditada.MesReaberto, $"Mês {Nome(mes)}", mes, "Fechado", fechamento.MotivoDaReabertura);
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>10/2026, como a tela mostra.</summary>
    public static string Nome(string competencia) => $"{competencia[5..7]}/{competencia[..4]}";
}
