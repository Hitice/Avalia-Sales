using System.Net.Mail;

namespace AvaliaSales.Domain;

public static class Emails
{
    public const int Maximo = 254;

    /// <summary>Em minúsculas e sem espaços; vazio vira nulo; formato errado é recusado.</summary>
    public static string? Normalizar(string? email)
    {
        var limpo = (email ?? string.Empty).Trim().ToLowerInvariant();
        if (limpo.Length == 0)
        {
            return null;
        }

        return limpo.Length <= Maximo && MailAddress.TryCreate(limpo, out var lido) && lido.Address == limpo && limpo.Contains('.', StringComparison.Ordinal)
            ? limpo
            : throw new RegraDeNegocioException("E-mail inválido.");
    }
}
