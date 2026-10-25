using AvaliaSales.Domain.Avisos;
using AvaliaSales.Infrastructure.Banco;
using AvaliaSales.Infrastructure.Idempotencia;
using AvaliaSales.Infrastructure.Integracoes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AvaliaSales.Infrastructure.Manutencao;

/// <summary>
/// O que vence e não serve mais: chaves de idempotência, sessões, links de
/// senha e e-mails antigos. Cada método é uma rotina do Worker (no próprio
/// escopo e <c>try</c>), todas apagam por data e por isso são seguras de repetir.
/// </summary>
public sealed class LimpezaDeExpirados(BancoDeDados banco, IServiceScopeFactory escopos, TimeProvider relogio)
{
    /// <summary>E-mail enviado (ou desistido) fica para consulta por este tempo; a chave dele só serve ao Resend nas primeiras 24 horas.</summary>
    public static readonly TimeSpan GuardaDoEmail = TimeSpan.FromDays(30);

    /// <summary>Link vencido ou usado fica para a auditoria do suporte por este tempo.</summary>
    public static readonly TimeSpan GuardaDoLink = TimeSpan.FromDays(7);

    /// <summary>
    /// O RLS da tabela mostra só as linhas do revendedor da sessão: a limpeza
    /// roda um revendedor por vez e depois a operadora, como as outras rotinas.
    /// </summary>
    public async Task<int> LimparChaves(CancellationToken ct)
    {
        var revendedores = await banco.Revendedores.AsNoTracking().Select(r => r.Id).ToListAsync(ct);
        var apagadas = 0;
        foreach (var id in revendedores)
        {
            apagadas += await LimparChavesDe(id, operadora: false, ct);
        }

        return apagadas + await LimparChavesDe(null, operadora: true, ct);
    }

    private async Task<int> LimparChavesDe(Guid? revendedorId, bool operadora, CancellationToken ct)
    {
        using var escopo = escopos.CreateScope();
        var execucao = escopo.ServiceProvider.GetRequiredService<RevendedorDaExecucao>();
        execucao.RevendedorId = revendedorId;
        execucao.Operadora = operadora;
        return await escopo.ServiceProvider.GetRequiredService<ChavesDeIdempotencia>().LimparVencidas(ct);
    }

    /// <summary>
    /// Só a família inteira vencida: enquanto algum login da família vive, o
    /// token antigo dela continua sendo achado e a reapresentação continua
    /// detectada como roubo.
    /// </summary>
    public Task<int> LimparSessoes(CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        return banco.Database.ExecuteSqlAsync($"""
            delete from sessoes s
            where not exists (select 1 from sessoes o where o.familia = s.familia and o.expira_em > {agora})
            """, ct);
    }

    public Task<int> LimparLinks(CancellationToken ct)
    {
        var limite = relogio.GetUtcNow() - GuardaDoLink;
        return banco.LinksDeSenha.Where(l => l.VenceEm < limite).ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// Primeiro descarta e limpa o conteúdo do e-mail sensível que não saiu a
    /// tempo; depois apaga o e-mail enviado, ou desistido, com mais de 30 dias.
    /// </summary>
    public async Task<int> LimparEmails(CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        var limiteDoSegredo = agora - EmailNaFila.ValidadeDoSegredo;
        var vencidos = await banco.Emails
            .Where(e => e.Sensivel && e.EnviadoEm == null && e.Tentativas < EmailNaFila.TentativasMaximas && e.CriadoEm < limiteDoSegredo)
            .Take(200)
            .ToListAsync(ct);
        var limpos = vencidos.Count(e => e.DescartarSeVenceu(agora));
        if (limpos > 0)
        {
            await banco.SaveChangesAsync(ct);
        }

        var limite = agora - GuardaDoEmail;
        var apagados = await banco.Emails
            .Where(e => e.CriadoEm < limite && (e.EnviadoEm != null || e.Tentativas >= EmailNaFila.TentativasMaximas))
            .ExecuteDeleteAsync(ct);
        return limpos + apagados;
    }
}
