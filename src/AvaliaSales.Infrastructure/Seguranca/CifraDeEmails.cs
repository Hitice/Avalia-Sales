using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AvaliaSales.Application.Avisos;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Infrastructure.Seguranca;

/// <summary>
/// AES-256-GCM com nonce aleatório por linha. Formato: <c>avalia-cifra:v1:</c> + base64(nonce 12 | tag 16 | cifra).
/// O conteúdo é o par [assunto, texto] em JSON. A chave do e-mail (única e imutável) vai como dado associado.
/// A versão no prefixo deixa trocar o formato depois sem quebrar as linhas que já estão na fila.
/// </summary>
public sealed class CifraDeEmails : ICifraDeEmails
{
    private const string Prefixo = "avalia-cifra:v1:";
    private const int Nonce = 12;
    private const int Tag = 16;

    private readonly byte[] _atual;
    private readonly byte[]? _anterior;

    public CifraDeEmails(IOptions<OpcoesDeSeguranca> opcoes)
    {
        _atual = OpcoesDeSeguranca.Ler(opcoes.Value.ChaveDosEmails) ?? OpcoesDeSeguranca.Ler(OpcoesDeSeguranca.ChaveDeDesenvolvimento)!;
        _anterior = OpcoesDeSeguranca.Ler(opcoes.Value.ChaveAnteriorDosEmails);
    }

    public bool EstaCifrado(string texto) => texto.StartsWith(Prefixo, StringComparison.Ordinal);

    public string Cifrar(string chaveDoEmail, string assunto, string texto)
    {
        var aberto = JsonSerializer.SerializeToUtf8Bytes(new[] { assunto, texto });
        var saida = new byte[Nonce + Tag + aberto.Length];
        var nonce = saida.AsSpan(0, Nonce);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_atual, Tag);
        aes.Encrypt(nonce, aberto, saida.AsSpan(Nonce + Tag), saida.AsSpan(Nonce, Tag), Encoding.UTF8.GetBytes(chaveDoEmail));
        CryptographicOperations.ZeroMemory(aberto);
        return Prefixo + Convert.ToBase64String(saida);
    }

    public (string Assunto, string Texto) Abrir(string chaveDoEmail, string cifrado)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(cifrado[Prefixo.Length..]);
        }
        catch (FormatException)
        {
            throw new CifraDeEmailInvalidaException();
        }

        if (bytes.Length < Nonce + Tag)
        {
            throw new CifraDeEmailInvalidaException();
        }

        var dadoAssociado = Encoding.UTF8.GetBytes(chaveDoEmail);
        foreach (var chave in new[] { _atual, _anterior })
        {
            if (chave is null)
            {
                continue;
            }

            var aberto = new byte[bytes.Length - Nonce - Tag];
            try
            {
                using var aes = new AesGcm(chave, Tag);
                aes.Decrypt(bytes.AsSpan(0, Nonce), bytes.AsSpan(Nonce + Tag), bytes.AsSpan(Nonce, Tag), aberto, dadoAssociado);
            }
            catch (CryptographicException)
            {
                continue;
            }

            var partes = JsonSerializer.Deserialize<string[]>(aberto);
            CryptographicOperations.ZeroMemory(aberto);
            return partes is { Length: 2 } ? (partes[0], partes[1]) : throw new CifraDeEmailInvalidaException();
        }

        throw new CifraDeEmailInvalidaException();
    }
}
