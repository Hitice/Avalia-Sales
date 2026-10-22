using System.Security.Cryptography;

namespace AvaliaSales.Tests.Apoio;

/// <summary>O código de 6 dígitos que o app autenticador mostraria (RFC 6238, HMAC-SHA1, 30 s).</summary>
public static class Totp
{
    public static string Codigo(string chaveBase32, DateTimeOffset? quando = null)
    {
        var passo = (quando ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / 30;
        Span<byte> contador = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(contador, passo);
        // SHA-1 é o que a RFC 6238 e os apps autenticadores usam; aqui só reproduz o app.
#pragma warning disable CA5350
        var hash = HMACSHA1.HashData(Base32(chaveBase32), contador);
#pragma warning restore CA5350
        var deslocamento = hash[^1] & 0x0f;
        var numero = ((hash[deslocamento] & 0x7f) << 24) | (hash[deslocamento + 1] << 16) | (hash[deslocamento + 2] << 8) | hash[deslocamento + 3];
        return (numero % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static byte[] Base32(string texto)
    {
        const string Alfabeto = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = 0;
        var valor = 0;
        var saida = new List<byte>();
        foreach (var c in texto.TrimEnd('=').ToUpperInvariant())
        {
            valor = (valor << 5) | Alfabeto.IndexOf(c, StringComparison.Ordinal);
            bits += 5;
            if (bits >= 8)
            {
                saida.Add((byte)(valor >> (bits - 8)));
                bits -= 8;
            }
        }

        return [.. saida];
    }
}
