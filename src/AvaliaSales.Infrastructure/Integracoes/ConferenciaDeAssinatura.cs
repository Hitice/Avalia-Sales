using AvaliaSales.Application;
using AvaliaSales.Application.Contratos;
using AvaliaSales.Application.Revendedores;
using AvaliaSales.Infrastructure.Banco;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Infrastructure.Integracoes;

/// <summary>
/// A reconsulta da D4Sign fora do webhook, na sessão do revendedor do contrato.
/// Lê a situação fora da transação (como o processador de eventos) e aplica com
/// a linha do contrato travada, a mesma trava do webhook.
/// </summary>
public sealed class ConferenciaDeAssinatura(BancoDeDados banco, IAssinaturaEletronica assinatura, EfeitosDaAssinatura efeitos, TimeProvider relogio)
    : IConferenciaDeAssinatura
{
    public async Task<bool> Conferir(Guid contratoId, bool pedidoDoDono, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        var antes = await banco.Contratos.AsNoTracking().SingleAsync(c => c.Id == contratoId, ct);
        if (pedidoDoDono)
        {
            antes.PodeConferir(agora);
        }

        Domain.Contratos.SituacaoDoContrato? lida;
        try
        {
            lida = await assinatura.LerSituacao(antes.DocumentoNaAssinatura ?? throw new Domain.RegraDeNegocioException("Documento não enviado para assinatura."), ct);
        }
        catch (IntegracaoException)
        {
            // Marca a tentativa: a próxima reconsulta espera o intervalo, e a cota da D4Sign não some numa queda.
            await banco.Contratos.Where(c => c.Id == contratoId).ExecuteUpdateAsync(s => s.SetProperty(c => c.ConferidoEm, agora), ct);
            throw;
        }

        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        await Travar(banco, contratoId, ct);
        var contrato = await banco.Contratos.SingleAsync(c => c.Id == contratoId, ct);
        contrato.RegistrarConferencia(agora);
        var mudou = lida is { } situacao && await efeitos.Aplicar(contrato, situacao, agora, ct);
        await banco.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
        return mudou;
    }

    /// <summary>Webhook, Worker e botão aplicam um de cada vez sobre o mesmo contrato.</summary>
    public static Task Travar(BancoDeDados alvo, Guid contratoId, CancellationToken ct) =>
        alvo.Database.SqlQuery<Guid>($"select id as \"Value\" from contratos where id = {contratoId} for update").ToListAsync(ct);
}
