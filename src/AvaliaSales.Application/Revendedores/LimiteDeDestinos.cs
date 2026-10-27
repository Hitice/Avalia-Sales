using AvaliaSales.Domain;
using AvaliaSales.Domain.Auditoria;
using AvaliaSales.Domain.Revendedores;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Revendedores;

public sealed record SituacaoDoLimite(int Usados, int Limite, bool Esgotado);

/// <summary>
/// Trava contra o uso do domínio da Avalia para endereço alheio: cada revenda cadastra até
/// <see cref="Revendedor.DestinosPorDia"/> destinos por dia (Brasília), contando placa vendida, destino trocado e link criado
/// ou alterado. Passou disso, a revenda pede liberação num chamado e a operadora libera mais naquele dia.
/// </summary>
public sealed class LimiteDeDestinos(IBancoDeDados banco, IRevendedorAtual atual, TimeProvider relogio, Atendimento.Chamados chamados, Notificacoes.Notificacoes notificacoes)
{
    private static readonly TimeZoneInfo Brasilia = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    private static readonly AcaoAuditada[] Contadas = [AcaoAuditada.DestinoDefinido, AcaoAuditada.DestinoTrocado, AcaoAuditada.LinkCriado, AcaoAuditada.LinkAlterado];

    public static DateOnly Hoje(TimeProvider relogio) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(relogio.GetUtcNow(), Brasilia).DateTime);

    public async Task<SituacaoDoLimite> Situacao(CancellationToken ct)
    {
        var hoje = Hoje(relogio);
        var revendedor = await banco.Revendedores.AsNoTracking().SingleAsync(r => r.Id == atual.RevendedorId, ct);
        var usados = await Usados(hoje, ct);
        var limite = revendedor.LimiteDeDestinosEm(hoje);
        return new SituacaoDoLimite(usados, limite, usados >= limite);
    }

    /// <summary>
    /// Recusa quando <paramref name="novos"/> destinos passariam do limite do dia. Chamar dentro da transação que grava o
    /// destino e a auditoria dele: a trava dos destinos da revenda vale até o commit, e duas gravações juntas contam uma
    /// depois da outra.
    /// </summary>
    public async Task Exigir(int novos, CancellationToken ct)
    {
        if (novos <= 0)
        {
            return;
        }

        if (banco.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("O limite de destinos é conferido dentro da transação que grava o destino.");
        }

        await banco.TravarLinksDaRevenda(atual.RevendedorId ?? throw new InvalidOperationException("Limite de destinos sem revenda na sessão."), ct);
        await Recusar(novos, ct);
    }

    /// <summary>
    /// A mesma conta sem a trava e fora da transação, antes da consulta ao Google Web Risk: o pedido que o limite já
    /// recusa não chega ao Google (ADR 0031). Quem vale é <see cref="Exigir"/>, na transação.
    /// </summary>
    public async Task ConferirAntes(int novos, CancellationToken ct)
    {
        if (novos > 0)
        {
            await Recusar(novos, ct);
        }
    }

    private async Task Recusar(int novos, CancellationToken ct)
    {
        var situacao = await Situacao(ct);
        if (situacao.Usados + novos > situacao.Limite)
        {
            throw new RegraDeNegocioException(
                $"Limite de {situacao.Limite} destinos por dia atingido. O revendedor pede mais em Solicitar liberação.");
        }
    }

    /// <summary>A revenda pede mais cadastros hoje: abre o chamado e avisa a operadora no sino.</summary>
    public async Task Solicitar(string? motivo, CancellationToken ct)
    {
        var situacao = await Situacao(ct);
        var revendedor = await banco.Revendedores.AsNoTracking().SingleAsync(r => r.Id == atual.RevendedorId, ct);
        var detalhe = string.IsNullOrWhiteSpace(motivo) ? string.Empty : $"\n\nMotivo: {motivo.Trim()}";
        await chamados.Abrir("Liberação de cadastros de destino",
            $"Usamos {situacao.Usados} de {situacao.Limite} cadastros de destino hoje e preciso de mais.{detalhe}", ct);
        notificacoes.ParaOperadora(revendedor.Id, "Liberação de cadastros pedida", $"{revendedor.Nome} atingiu o limite diário de destinos.", "/operadora/revendedores");
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>Pela operadora: soma <paramref name="quantidade"/> ao limite de hoje da revenda e avisa o dono.</summary>
    public async Task Liberar(Guid revendedorId, int quantidade, CancellationToken ct)
    {
        var revendedor = await banco.Revendedores.SingleOrDefaultAsync(r => r.Id == revendedorId, ct) ?? throw new RegraDeNegocioException("Revenda não encontrada.");
        revendedor.LiberarDestinos(quantidade, Hoje(relogio));
        notificacoes.ParaDono(revendedor.Id, "Cadastros liberados", $"A Avalia liberou mais {quantidade} cadastros de destino para hoje.", "/vender");
        await banco.SaveChangesAsync(ct);
    }

    private async Task<int> Usados(DateOnly hoje, CancellationToken ct)
    {
        var meiaNoite = hoje.ToDateTime(TimeOnly.MinValue);
        var inicio = new DateTimeOffset(meiaNoite, Brasilia.GetUtcOffset(meiaNoite)).ToUniversalTime();
        return await banco.Auditoria.AsNoTracking().CountAsync(a => Contadas.Contains(a.Acao) && a.OcorridoEm >= inicio, ct);
    }
}
