using AvaliaSales.Infrastructure.Banco;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Infrastructure.Idempotencia;

public enum DesfechoDaReserva
{
    /// <summary>A chave é desta requisição: executar e depois concluir ou liberar.</summary>
    Executar,

    /// <summary>Já concluída com o mesmo corpo: devolver a resposta gravada.</summary>
    Repetir,

    /// <summary>A chave já foi usada com outro corpo.</summary>
    CorpoDiferente,

    /// <summary>Outra requisição segue executando e não terminou no prazo de espera.</summary>
    EmExecucao,
}

public sealed record ReservaDaChave(DesfechoDaReserva Desfecho, Guid Id, ChaveDeIdempotencia? Gravada);

/// <summary>
/// Reserva, conclui e libera as chaves de idempotência. A unicidade do
/// Postgres decide quem executa: <c>insert ... on conflict do nothing</c> devolve
/// uma linha para um só. Só SQL parametrizado e consultas sem rastreamento, para
/// não misturar a chave com as entidades que o caso de uso da rota altera.
/// </summary>
public sealed class ChavesDeIdempotencia(BancoDeDados banco, TimeProvider relogio)
{
    public static readonly TimeSpan Validade = TimeSpan.FromHours(24);

    /// <summary>Uma execução some depois disso sem concluir (processo caiu): outra requisição pode assumir a chave.</summary>
    public static readonly TimeSpan Bloqueio = TimeSpan.FromMinutes(2);

    /// <summary>Quanto a repetição espera a execução em curso antes de responder 409.</summary>
    public static readonly TimeSpan EsperaMaxima = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan Passo = TimeSpan.FromMilliseconds(100);

    public async Task<ReservaDaChave> Reservar(Guid usuarioId, Guid? revendedorId, string rota, string chave, string hashDoCorpo, CancellationToken ct)
    {
        var limite = relogio.GetUtcNow() + EsperaMaxima;
        while (true)
        {
            var agora = relogio.GetUtcNow();

            // Chave vencida e ainda não varrida pelo Worker não vale: sai do caminho antes de reservar.
            await banco.Database.ExecuteSqlAsync($"""
                delete from chaves_de_idempotencia
                where usuario_id = {usuarioId} and rota = {rota} and chave = {chave} and expira_em <= {agora}
                """, ct);

            var id = Guid.CreateVersion7();
            var gravadas = await banco.Database.ExecuteSqlAsync($"""
                insert into chaves_de_idempotencia
                    (id, usuario_id, revendedor_id, rota, chave, hash_do_corpo, situacao, criada_em, bloqueada_ate, expira_em)
                values
                    ({id}, {usuarioId}, {revendedorId}, {rota}, {chave}, {hashDoCorpo}, {nameof(SituacaoDaChave.EmAndamento)}, {agora}, {agora + Bloqueio}, {agora + Validade})
                on conflict (usuario_id, rota, chave) do nothing
                """, ct);
            if (gravadas == 1)
            {
                return new ReservaDaChave(DesfechoDaReserva.Executar, id, null);
            }

            var existente = await banco.ChavesDeIdempotencia.AsNoTracking()
                .SingleOrDefaultAsync(c => c.UsuarioId == usuarioId && c.Rota == rota && c.Chave == chave, ct);
            if (existente is null)
            {
                // Liberada entre o insert e a leitura: tenta reservar de novo.
                continue;
            }

            if (existente.HashDoCorpo != hashDoCorpo)
            {
                return new ReservaDaChave(DesfechoDaReserva.CorpoDiferente, existente.Id, existente);
            }

            if (existente.Situacao == SituacaoDaChave.Concluida)
            {
                return new ReservaDaChave(DesfechoDaReserva.Repetir, existente.Id, existente);
            }

            if (existente.BloqueadaAte <= agora)
            {
                var assumida = await banco.Database.ExecuteSqlAsync($"""
                    update chaves_de_idempotencia set bloqueada_ate = {agora + Bloqueio}
                    where id = {existente.Id} and situacao = {nameof(SituacaoDaChave.EmAndamento)} and bloqueada_ate <= {agora}
                    """, ct);
                if (assumida == 1)
                {
                    return new ReservaDaChave(DesfechoDaReserva.Executar, existente.Id, null);
                }

                continue;
            }

            if (agora >= limite)
            {
                return new ReservaDaChave(DesfechoDaReserva.EmExecucao, existente.Id, existente);
            }

            await Task.Delay(Passo, relogio, ct);
        }
    }

    public Task Concluir(Guid id, int status, string? tipoDeConteudo, string? local, byte[] corpo, CancellationToken ct)
    {
        var expira = relogio.GetUtcNow() + Validade;
        return banco.Database.ExecuteSqlAsync($"""
            update chaves_de_idempotencia
            set situacao = {nameof(SituacaoDaChave.Concluida)}, status_http = {status}, tipo_de_conteudo = {tipoDeConteudo},
                local = {local}, corpo = {corpo}, expira_em = {expira}
            where id = {id}
            """, ct);
    }

    /// <summary>A execução não deixou resposta que valha repetir (exceção, 5xx, 429): a chave volta a ficar livre.</summary>
    public Task Liberar(Guid id, CancellationToken ct) =>
        banco.Database.ExecuteSqlAsync($"delete from chaves_de_idempotencia where id = {id} and situacao = {nameof(SituacaoDaChave.EmAndamento)}", ct);

    /// <summary>Apaga o que venceu das linhas que o RLS da sessão atual enxerga (um revendedor, ou a operadora).</summary>
    public async Task<int> LimparVencidas(CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        return await banco.ChavesDeIdempotencia.Where(c => c.ExpiraEm <= agora).ExecuteDeleteAsync(ct);
    }
}
