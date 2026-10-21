using System.ComponentModel.DataAnnotations;

namespace AvaliaSales.Infrastructure.Integracoes.Pagarme;

public sealed class OpcoesPagarme
{
    public const string Secao = "Pagarme";

    /// <summary>Teste e produção usam o mesmo endereço; a chave define o ambiente.</summary>
    [Required, Url] public string UrlBase { get; init; } = "https://api.pagar.me/core/v5/";

    /// <summary>sk_test_ em teste, sk_ em produção. Vem do ambiente.</summary>
    [Required] public string ChaveSecreta { get; init; } = string.Empty;

    [Required, RegularExpression("^rp_[A-Za-z0-9]+$")] public string RecebedorAvalia { get; init; } = string.Empty;

    /// <summary>
    /// Segredo na URL do webhook (/webhooks/pagarme/{segredo}). O Pagar.me não
    /// assina o webhook; o segredo barra quem não sabe a URL, e a reconsulta
    /// do pedido barra o resto.
    /// </summary>
    [Required, MinLength(32)] public string SegredoDoWebhook { get; init; } = string.Empty;

    /// <summary>Texto na fatura do cartão; o Pagar.me aceita até 13 caracteres para conta PSP.</summary>
    [Required, MaxLength(13)] public string DescricaoNaFatura { get; init; } = "AVALIA SALES";

    [Range(5, 1440)] public int ValidadeDoPixEmMinutos { get; init; } = 30;
}
