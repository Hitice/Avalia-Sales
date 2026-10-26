using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Infrastructure.Seguranca;

/// <summary>
/// Chaves de proteção que a API e o Worker precisam ter iguais. <see cref="ChaveDosEmails"/> cifra os e-mails
/// sensíveis na fila; <see cref="ChaveAnteriorDosEmails"/> só abre o que a chave antiga cifrou, durante a rotação.
/// Cada uma é 32 bytes em base64 (<c>openssl rand -base64 32</c>).
/// </summary>
public sealed class OpcoesDeSeguranca
{
    public const string Secao = "Seguranca";

    public const int TamanhoDaChave = 32;

    /// <summary>Só vale em Development e nos testes. Em qualquer outro ambiente a chave é obrigatória.</summary>
    public const string ChaveDeDesenvolvimento = "Y2hhdmUtc28tZGUtZGVzZW52b2x2aW1lbnRvLTAwMDE=";

    public string? ChaveDosEmails { get; set; }

    public string? ChaveAnteriorDosEmails { get; set; }

    public static byte[]? Ler(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return null;
        }

        var destino = new byte[TamanhoDaChave + 8];
        return Convert.TryFromBase64String(base64.Trim(), destino, out var n) && n == TamanhoDaChave ? destino[..n] : null;
    }
}

/// <summary>Falha a subida (API e Worker) se a chave faltar fora do Development ou não for de 32 bytes em base64.</summary>
public sealed class ValidadorDasOpcoesDeSeguranca(IServiceProvider servicos) : IValidateOptions<OpcoesDeSeguranca>
{
    public ValidateOptionsResult Validate(string? name, OpcoesDeSeguranca options)
    {
        var erros = new List<string>();
        var ambiente = servicos.GetService(typeof(IHostEnvironment)) as IHostEnvironment;
        var desenvolvimento = ambiente is null || ambiente.IsDevelopment();

        if (string.IsNullOrWhiteSpace(options.ChaveDosEmails))
        {
            if (!desenvolvimento)
            {
                erros.Add($"Falta {OpcoesDeSeguranca.Secao}:{nameof(OpcoesDeSeguranca.ChaveDosEmails)} (32 bytes em base64: openssl rand -base64 32), igual na API e no Worker.");
            }
        }
        else if (OpcoesDeSeguranca.Ler(options.ChaveDosEmails) is null)
        {
            erros.Add($"{OpcoesDeSeguranca.Secao}:{nameof(OpcoesDeSeguranca.ChaveDosEmails)} tem de ser de {OpcoesDeSeguranca.TamanhoDaChave} bytes em base64.");
        }

        if (!string.IsNullOrWhiteSpace(options.ChaveAnteriorDosEmails) && OpcoesDeSeguranca.Ler(options.ChaveAnteriorDosEmails) is null)
        {
            erros.Add($"{OpcoesDeSeguranca.Secao}:{nameof(OpcoesDeSeguranca.ChaveAnteriorDosEmails)} tem de ser de {OpcoesDeSeguranca.TamanhoDaChave} bytes em base64.");
        }

        return erros.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(erros);
    }
}
