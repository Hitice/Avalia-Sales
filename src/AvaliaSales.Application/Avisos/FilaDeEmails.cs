using AvaliaSales.Domain.Avisos;

namespace AvaliaSales.Application.Avisos;

/// <summary>
/// Quem assina o e-mail que sai em nome de uma revenda: o nome dela no
/// remetente, a resposta para o dono e a cor da marca no botão. O endereço de
/// envio continua o da plataforma, o único com domínio verificado no Resend.
/// </summary>
public sealed record RemetenteDoEmail(string Nome, string? ResponderPara, string? Cor);

/// <summary>A mensagem pronta para o Resend; sem remetente, sai em nome da plataforma.</summary>
public sealed record MensagemDeEmail(string Chave, string Para, string Assunto, string Texto, RemetenteDoEmail? Remetente);

/// <summary>
/// Põe o e-mail na fila, na mesma transação do fato que o motiva. Quem envia é
/// o Worker; a chave impede o mesmo aviso duas vezes. E-mail com link de senha
/// ou código de acesso é <c>sensivel</c>: assunto e texto ficam cifrados na fila e o conteúdo sai do banco depois do envio.
/// </summary>
public sealed class FilaDeEmails(IBancoDeDados banco, TimeProvider relogio, ICifraDeEmails cifra)
{
    public void Enfileirar(string chave, string para, string assunto, string texto, RemetenteDoEmail? remetente = null, bool sensivel = false)
    {
        var email = sensivel
            ? new EmailNaFila(chave, para, ICifraDeEmails.AssuntoNaFila, cifra.Cifrar(chave, assunto, texto), relogio.GetUtcNow(), sensivel: true)
            : new EmailNaFila(chave, para, assunto, texto, relogio.GetUtcNow());
        if (remetente is not null)
        {
            email.AssinarComo(remetente.Nome, remetente.ResponderPara, remetente.Cor);
        }

        banco.Emails.Add(email);
    }
}

/// <summary>O envio de fato, pelo Resend. A chave vai como Idempotency-Key.</summary>
public interface IEnvioDeEmail
{
    bool Configurado { get; }

    Task<string> Enviar(MensagemDeEmail mensagem, CancellationToken ct);
}
