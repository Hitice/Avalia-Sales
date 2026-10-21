using System.ComponentModel.DataAnnotations;

namespace AvaliaSales.Application;

/// <summary>Endereços e contatos da plataforma, iguais para todos os revendedores.</summary>
public sealed class OpcoesDaPlataforma
{
    public const string Secao = "Plataforma";

    /// <summary>Endereço gravado no QR, como https://avaliaone.com.br. Mudar quebra as placas impressas.</summary>
    [Required, Url]
    public string UrlPublica { get; set; } = string.Empty;

    /// <summary>Endereço do front, para o link dos e-mails e para a placa em estoque abrir a Nova venda.</summary>
    [Required, Url]
    public string UrlDoPainel { get; set; } = string.Empty;

    /// <summary>Quem recebe o aviso de pedido de placas na Avalia.</summary>
    [Required, EmailAddress]
    public string EmailDaOperadora { get; set; } = string.Empty;

    public string HostPublico => new Uri(UrlPublica).Host;

    /// <summary>O endereço gravado no QR e no NFC, em maiúsculas para o modo alfanumérico do QR.</summary>
    public string EnderecoDaPlaca(string codigo) => EnderecoDaPlaca(UrlPublica, codigo);

    public static string EnderecoDaPlaca(string urlPublica, string codigo) =>
        (urlPublica.TrimEnd('/') + "/Q/" + codigo).ToUpperInvariant();
}
