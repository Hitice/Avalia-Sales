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
    /// pk_test_ ou pk_, a chave pública: o navegador manda o cartão direto ao
    /// Pagar.me e a API recebe só o token. Vazia, a venda é só no Pix.
    /// </summary>
    [RegularExpression("^$|^pk_[A-Za-z0-9_]+$")] public string ChavePublica { get; init; } = string.Empty;

    /// <summary>
    /// Segredo na URL do webhook (/webhooks/pagarme/{segredo}). O Pagar.me não
    /// assina o webhook; o segredo barra quem não sabe a URL, e a reconsulta
    /// do pedido barra o resto.
    /// </summary>
    [Required, MinLength(32)] public string SegredoDoWebhook { get; init; } = string.Empty;

    /// <summary>Texto na fatura do cartão; o Pagar.me aceita até 13 caracteres para conta PSP.</summary>
    [Required, MaxLength(13)] public string DescricaoNaFatura { get; init; } = "AVALIA SALES";

    [Range(5, 1440)] public int ValidadeDoPixEmMinutos { get; init; } = 30;

    /// <summary>
    /// Intervalo da reconsulta de cobrança pendente, a rede de segurança do
    /// webhook perdido. Em produção, minutos; no simulador, segundos.
    /// </summary>
    [Range(5, 3600)] public int ConferenciaEmSegundos { get; init; } = 300;

    /// <summary>
    /// Só em Development: troca o Pagar.me por um simulador sem rede, que
    /// aprova o Pix de até R$ 500,00 depois de alguns segundos, como o
    /// simulador de Pix do sandbox. A API recusa subir com ele fora de Development.
    /// </summary>
    public bool Simulado { get; init; }
}
