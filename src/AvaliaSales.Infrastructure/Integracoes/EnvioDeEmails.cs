using AvaliaSales.Application;
using AvaliaSales.Application.Avisos;
using AvaliaSales.Infrastructure.Banco;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AvaliaSales.Infrastructure.Integracoes;

/// <summary>
/// Esvazia a fila de e-mails. Sem Resend configurado, não faz nada: o e-mail
/// espera, em vez de ser marcado como enviado sem ter saído. Dois Workers no
/// mesmo e-mail chegam ao Resend com a mesma Idempotency-Key, e ele entrega
/// uma vez só.
/// </summary>
public sealed partial class EnvioDeEmails(BancoDeDados banco, IEnvioDeEmail envio, ICifraDeEmails cifra, TimeProvider relogio, ILogger<EnvioDeEmails> log)
{
    public async Task<int> Executar(int lote, CancellationToken ct)
    {
        if (!envio.Configurado)
        {
            return 0;
        }

        var agora = relogio.GetUtcNow();
        var pendentes = await banco.Emails
            .Where(e => e.EnviadoEm == null && e.Tentativas < Domain.Avisos.EmailNaFila.TentativasMaximas
                && (e.ProximaTentativaEm == null || e.ProximaTentativaEm <= agora))
            .OrderBy(e => e.CriadoEm)
            .Take(lote)
            .ToListAsync(ct);

        var enviados = 0;
        foreach (var email in pendentes)
        {
            try
            {
                var remetente = email.NomeDoRemetente is { } nome ? new RemetenteDoEmail(nome, email.ResponderPara, email.CorDaMarca) : null;
                var (assunto, texto) = email.Sensivel && cifra.EstaCifrado(email.Texto) ? cifra.Abrir(email.Chave, email.Texto) : (email.Assunto, email.Texto);
                email.MarcarEnviado(await envio.Enviar(new MensagemDeEmail(email.Chave, email.Para, assunto, texto, remetente), ct), relogio.GetUtcNow());
                enviados++;
            }
            catch (CifraDeEmailInvalidaException)
            {
                // Repetir não adianta e o log não leva o conteúdo: só o id.
                LogCifraInvalida(log, email.Id);
                email.FalharDefinitivamente("Conteúdo cifrado não abre com a chave configurada: confira Seguranca__ChaveDosEmails na API e no Worker.");
            }
            catch (IntegracaoException erro)
            {
                LogFalha(log, email.Id, erro);
                email.RegistrarFalha(erro.Message, relogio.GetUtcNow());
            }

            await banco.SaveChangesAsync(ct);
        }

        return enviados;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "E-mail {Id} cifrado não abriu com a chave configurada e foi descartado")]
    private static partial void LogCifraInvalida(ILogger log, Guid id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "E-mail {Id} não saiu e volta para a fila")]
    private static partial void LogFalha(ILogger log, Guid id, Exception erro);
}
