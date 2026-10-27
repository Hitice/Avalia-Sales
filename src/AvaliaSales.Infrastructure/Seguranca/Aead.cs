using System.Security.Cryptography;

namespace AvaliaSales.Infrastructure.Seguranca;

/// <summary>AES-256-GCM com nonce aleatório. Saída: nonce (12) | tag (16) | cifra. Base da cifra dos e-mails e dos segredos do usuário.</summary>
internal static class Aead
{
    private const int Nonce = 12;
    private const int Tag = 16;

    public static byte[] Cifrar(byte[] chave, ReadOnlySpan<byte> aberto, ReadOnlySpan<byte> associado)
    {
        var saida = new byte[Nonce + Tag + aberto.Length];
        var nonce = saida.AsSpan(0, Nonce);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(chave, Tag);
        aes.Encrypt(nonce, aberto, saida.AsSpan(Nonce + Tag), saida.AsSpan(Nonce, Tag), associado);
        return saida;
    }

    /// <summary>Nulo quando a chave não abre ou o valor não tem o tamanho mínimo.</summary>
    public static byte[]? Abrir(byte[] chave, byte[] cifrado, ReadOnlySpan<byte> associado)
    {
        if (cifrado.Length < Nonce + Tag)
        {
            return null;
        }

        var aberto = new byte[cifrado.Length - Nonce - Tag];
        try
        {
            using var aes = new AesGcm(chave, Tag);
            aes.Decrypt(cifrado.AsSpan(0, Nonce), cifrado.AsSpan(Nonce + Tag), cifrado.AsSpan(Nonce, Tag), aberto, associado);
            return aberto;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
