using System.Security.Cryptography;
using System.Text;

namespace AvaliaSales.Infrastructure.Integracoes.D4Sign;

/// <summary>
/// A D4Sign envia "Content-Hmac: sha256=&lt;hex&gt;", o HMAC-SHA256 do UUID do
/// documento com a secret key MAC da conta. Cobre só o UUID, e não o corpo:
/// o que o webhook diz é reconsultado na API antes de valer.
/// </summary>
public static class AssinaturaDoWebhook
{
    private const string Prefixo = "sha256=";

    public static bool Valida(string? uuidDoDocumento, string? cabecalho, string segredo)
    {
        if (string.IsNullOrEmpty(uuidDoDocumento) || cabecalho is null
            || !cabecalho.StartsWith(Prefixo, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var esperado = Calcular(uuidDoDocumento, segredo);
        var recebido = Encoding.ASCII.GetBytes(cabecalho[Prefixo.Length..].Trim().ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(esperado), recebido);
    }

    public static string Calcular(string uuidDoDocumento, string segredo) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(segredo), Encoding.UTF8.GetBytes(uuidDoDocumento)));
}
