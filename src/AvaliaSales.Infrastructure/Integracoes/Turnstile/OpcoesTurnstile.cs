namespace AvaliaSales.Infrastructure.Integracoes.Turnstile;

/// <summary>
/// Cloudflare Turnstile no cadastro público. Sem a chave secreta o captcha fica
/// desligado (Development, testes e produção até o dono ligar); com ela, o
/// cadastro exige o token do widget e recusa se o verificador não responder.
/// </summary>
public sealed class OpcoesTurnstile
{
    public const string Secao = "Turnstile";

    /// <summary>Do painel da Cloudflare (Turnstile), em Turnstile__ChaveSecreta. Nunca vai ao front.</summary>
    public string ChaveSecreta { get; init; } = string.Empty;

    public string UrlDeVerificacao { get; init; } = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

    public int TempoLimiteEmSegundos { get; init; } = 5;

    public bool Ligado => !string.IsNullOrWhiteSpace(ChaveSecreta);
}
