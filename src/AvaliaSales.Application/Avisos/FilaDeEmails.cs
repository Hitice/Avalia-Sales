using AvaliaSales.Domain.Avisos;

namespace AvaliaSales.Application.Avisos;

/// <summary>
/// Põe o e-mail na fila, na mesma transação do fato que o motiva. Quem envia é
/// o Worker; a chave impede o mesmo aviso duas vezes.
/// </summary>
public sealed class FilaDeEmails(IBancoDeDados banco, TimeProvider relogio)
{
    public void Enfileirar(string chave, string para, string assunto, string texto) =>
        banco.Emails.Add(new EmailNaFila(chave, para, assunto, texto, relogio.GetUtcNow()));
}

/// <summary>O envio de fato, pelo Resend. A chave vai como Idempotency-Key.</summary>
public interface IEnvioDeEmail
{
    bool Configurado { get; }

    Task<string> Enviar(string chave, string para, string assunto, string texto, CancellationToken ct);
}
