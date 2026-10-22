using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace AvaliaSales.Infrastructure.Integracoes.Resend;

/// <summary>
/// O HTML que acompanha o texto do e-mail, montado do próprio texto:
/// - linha "Rótulo: https://..." vira botão com esse rótulo, na cor da marca;
/// - linha "Rótulo: 123456" mostra o código em destaque;
/// - o resto vira parágrafo, com os links clicáveis.
/// Assim o texto puro continua legível e quem escreve o e-mail não mexe em HTML.
/// Tabela e estilo em linha, que é o que os clientes de e-mail entendem. O logo
/// vai em PNG: Gmail e Outlook não mostram SVG.
/// </summary>
public static partial class ModeloDeEmail
{
    private const string CorDaPlataforma = "#120c2b";
    private const string CorSecundaria = "#4d4766";

    /// <summary>O traço da marca (--traco do front), com a cor do meio para quem não desenha degradê.</summary>
    private const string Traco = "background-color:#a646e0;background-image:linear-gradient(90deg,#ee46bc 0%,#a646e0 38%,#7a3cf0 58%,#465fff 100%)";

    /// <param name="assunto">Assunto, que vai no título do HTML.</param>
    /// <param name="texto">O texto puro do e-mail, de onde sai o corpo.</param>
    /// <param name="nome">Nome da revenda que envia; nulo é e-mail da plataforma, com o logo.</param>
    /// <param name="cor">Cor da marca da revenda em #rrggbb; nula usa a da plataforma.</param>
    /// <param name="urlDoLogo">PNG do logo da plataforma em endereço absoluto; nulo escreve o nome.</param>
    public static string Html(string assunto, string texto, string? nome, string? cor, string? urlDoLogo = null)
    {
        var fundo = cor ?? CorDaPlataforma;
        var letra = ContrasteComBranco(fundo) >= 4.5 ? "#ffffff" : CorDaPlataforma;
        var corpo = new StringBuilder();
        foreach (var paragrafo in texto.Replace("\r\n", "\n", StringComparison.Ordinal).Trim().Split("\n\n"))
        {
            var comuns = new List<string>();
            foreach (var linha in paragrafo.Split('\n').Select(l => l.Trim()))
            {
                if (Botao().Match(linha) is { Success: true } botao)
                {
                    Paragrafo(corpo, comuns);
                    var href = WebUtility.HtmlEncode(botao.Groups["url"].Value);
                    var rotulo = botao.Groups["rotulo"].Success ? WebUtility.HtmlEncode(botao.Groups["rotulo"].Value.Trim()) : "Abrir";
                    corpo.Append(CultureInfo.InvariantCulture,
                        $"<p style=\"margin:8px 0 24px\"><a href=\"{href}\" style=\"display:inline-block;background:{fundo};color:{letra};text-decoration:none;font-weight:600;padding:14px 24px;border-radius:8px\">{rotulo}</a></p>");
                }
                else if (Codigo().Match(linha) is { Success: true } codigo)
                {
                    Paragrafo(corpo, comuns);
                    corpo.Append(CultureInfo.InvariantCulture,
                        $"<p style=\"margin:0 0 8px\">{WebUtility.HtmlEncode(codigo.Groups["rotulo"].Value.Trim())}</p>");
                    corpo.Append(CultureInfo.InvariantCulture,
                        $"<p style=\"margin:0 0 24px\"><span style=\"display:inline-block;font:700 28px/1 ui-monospace,Menlo,Consolas,monospace;letter-spacing:6px;padding:14px 20px;border-radius:8px;background:#f5f3fb;border:1px solid #e4e0f0\">{codigo.Groups["codigo"].Value}</span></p>");
                }
                else
                {
                    comuns.Add(linha);
                }
            }

            Paragrafo(corpo, comuns);
        }

        var topo = nome is null && urlDoLogo is not null
            ? $"<img src=\"{WebUtility.HtmlEncode(urlDoLogo)}\" width=\"200\" alt=\"Avalia Sales\" style=\"display:block;width:200px;max-width:100%;height:auto;border:0\">"
            : $"<span style=\"font-weight:700;font-size:20px\">{WebUtility.HtmlEncode(nome ?? "Avalia Sales")}</span>";
        return $"""
            <!doctype html>
            <html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width"><meta name="color-scheme" content="light"><title>{WebUtility.HtmlEncode(assunto)}</title></head>
            <body style="margin:0;background:#f5f3fb;font:16px/1.5 -apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;color:{CorDaPlataforma}">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f5f3fb"><tr><td align="center" style="padding:32px 12px">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;background:#ffffff;border-radius:12px;overflow:hidden">
            <tr><td style="height:6px;line-height:6px;font-size:0;{(cor is null ? Traco : $"background-color:{cor}")}">&nbsp;</td></tr>
            <tr><td style="padding:28px 32px 8px">{topo}</td></tr>
            <tr><td style="padding:16px 32px 16px">{corpo}</td></tr>
            </table>
            <p style="margin:20px 0 0;font-size:12px;color:{CorSecundaria}">Enviado pela plataforma Avalia Sales.</p>
            </td></tr></table>
            </body></html>
            """;
    }

    private static void Paragrafo(StringBuilder corpo, List<string> linhas)
    {
        if (linhas.Count == 0)
        {
            return;
        }

        corpo.Append("<p style=\"margin:0 0 16px\">")
            .Append(string.Join("<br>", linhas.Select(ComLinks)))
            .Append("</p>");
        linhas.Clear();
    }

    private static string ComLinks(string linha) =>
        Link().Replace(WebUtility.HtmlEncode(linha), m => $"<a href=\"{m.Value}\" style=\"color:{CorDaPlataforma}\">{m.Value}</a>");

    /// <summary>Razão de contraste WCAG entre a cor e o branco, para escolher a cor da letra do botão.</summary>
    private static double ContrasteComBranco(string hex)
    {
        static double Canal(string h) => int.Parse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0 is var c && c <= 0.03928
            ? c / 12.92
            : Math.Pow((c + 0.055) / 1.055, 2.4);
        var luz = 0.2126 * Canal(hex[1..3]) + 0.7152 * Canal(hex[3..5]) + 0.0722 * Canal(hex[5..7]);
        return 1.05 / (luz + 0.05);
    }

    /// <summary>"Criar senha nova: https://..." ou só o endereço na linha.</summary>
    [GeneratedRegex(@"^(?:(?<rotulo>[^:]{2,40}):\s*)?(?<url>https?://\S+)$")]
    private static partial Regex Botao();

    [GeneratedRegex(@"^(?<rotulo>.{2,80}):\s*(?<codigo>\d{6})$")]
    private static partial Regex Codigo();

    [GeneratedRegex(@"https?://[^\s<]+")]
    private static partial Regex Link();
}
