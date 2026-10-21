using System.ComponentModel.DataAnnotations;

namespace AvaliaSales.Infrastructure.Integracoes.D4Sign;

public sealed class OpcoesD4Sign
{
    public const string Secao = "D4Sign";

    /// <summary>Sandbox por padrão: produção (secure.d4sign.com.br) só com configuração explícita.</summary>
    [Required, Url] public string UrlBase { get; init; } = "https://sandbox.d4sign.com.br/api/v1/";

    [Required] public string TokenApi { get; init; } = string.Empty;

    /// <summary>Só é enviado se estiver habilitado na conta.</summary>
    public string? CryptKey { get; init; }

    /// <summary>Secret key MAC da conta: assina o UUID do documento em cada webhook.</summary>
    [Required, MinLength(16)] public string SegredoHmac { get; init; } = string.Empty;

    /// <summary>UUID do cofre onde os contratos ficam.</summary>
    [Required] public string Cofre { get; init; } = string.Empty;

    /// <summary>Endereço público do nosso webhook, em HTTPS.</summary>
    [Required, Url] public string UrlDoWebhook { get; init; } = string.Empty;
}
