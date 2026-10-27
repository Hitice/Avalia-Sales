using AvaliaSales.Domain.Avisos;

namespace AvaliaSales.Application.Avisos;

/// <summary>
/// Quem assina o e-mail que sai em nome de uma revenda: o nome dela no
/// remetente, a resposta para o dono e a cor da marca no botão. O endereço de
/// envio continua o da plataforma, o único com domínio verificado no Resend.
/// </summary>
public sealed record RemetenteDoEmail(string Nome, string? ResponderPara, string? Cor);

/// <summary>
/// A mensagem pronta para o Resend; sem remetente, sai em nome da plataforma. <c>Destaques</c> são os hashes dos
/// endereços e códigos que o servidor marcou (<see cref="DestaquesDoEmail"/>); sem eles, nada vira botão nem link.
/// </summary>
public sealed record MensagemDeEmail(string Chave, string Para, string Assunto, string Texto, RemetenteDoEmail? Remetente, IReadOnlySet<string>? Destaques = null);

/// <summary>
/// Põe o e-mail na fila, na mesma transação do fato que o motiva. Quem envia é
/// o Worker; a chave impede o mesmo aviso duas vezes. E-mail com link de senha
/// ou código de acesso é <c>sensivel</c>: assunto e texto ficam cifrados na fila e o conteúdo sai do banco depois do envio.
/// </summary>
public sealed class FilaDeEmails(IBancoDeDados banco, TimeProvider relogio, ICifraDeEmails cifra)
{
    /// <summary>
    /// <c>destaques</c> são os endereços e códigos montados pelo servidor. A linha "Rótulo: endereço" com um deles vira
    /// botão, e "Rótulo: código", código em destaque; qualquer outro endereço do texto sai como texto (<see cref="DestaquesDoEmail"/>).
    /// </summary>
    public void Enfileirar(string chave, string para, string assunto, string texto, RemetenteDoEmail? remetente = null, bool sensivel = false,
        IReadOnlyCollection<string>? destaques = null)
    {
        texto = DestaquesDoEmail.Anexar(texto, destaques);
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
