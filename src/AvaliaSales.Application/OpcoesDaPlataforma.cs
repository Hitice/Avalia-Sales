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

    /// <summary>
    /// CNPJ da revenda da Avalia, que recebe os clientes ativos de quem encerra a
    /// conta (distrato, cláusula 2). Sem ela cadastrada, o encerramento não transfere.
    /// </summary>
    public string CnpjDaRevendaDaAvalia { get; set; } = "68715987000164";

    /// <summary>Quem assina pela Avalia: o nome sai impresso no contrato e no distrato, já aceitos na emissão.</summary>
    public string? RepresentanteDaAvalia { get; set; }

    public string? CargoDoRepresentante { get; set; }

    /// <summary>Cadastro de operadora pela tela de entrada, temporário. Fechado por padrão: abrir só para criar a conta e fechar em seguida.</summary>
    public bool CadastroDeOperadoraAberto { get; set; }

    /// <summary>Só e-mail deste domínio cria conta de operadora pela tela.</summary>
    public string DominioDaOperadora { get; set; } = "avaliaone.com.br";

    public string HostPublico => new Uri(UrlPublica).Host;

    /// <summary>O endereço gravado no QR e no NFC, em maiúsculas para o modo alfanumérico do QR.</summary>
    public string EnderecoDaPlaca(string codigo) => EnderecoDaPlaca(UrlPublica, codigo);

    public static string EnderecoDaPlaca(string urlPublica, string codigo) =>
        (urlPublica.TrimEnd('/') + "/Q/" + codigo).ToUpperInvariant();

    /// <summary>O link curto, no mesmo formato do endereço da placa.</summary>
    public string EnderecoDoLink(string codigo) => (UrlPublica.TrimEnd('/') + "/L/" + codigo).ToUpperInvariant();
}
