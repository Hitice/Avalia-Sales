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
        var cifrado = Aead.Cifrar(_atual, aberto, Encoding.UTF8.GetBytes(chaveDoEmail));
        CryptographicOperations.ZeroMemory(aberto);
        return Prefixo + Convert.ToBase64String(cifrado);
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

        var dadoAssociado = Encoding.UTF8.GetBytes(chaveDoEmail);
        foreach (var chave in new[] { _atual, _anterior })
        {
            if (chave is null || Aead.Abrir(chave, bytes, dadoAssociado) is not { } aberto)
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
