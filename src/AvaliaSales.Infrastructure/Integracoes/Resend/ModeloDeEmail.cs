using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace AvaliaSales.Infrastructure.Integracoes.Resend;

/// <summary>
/// O HTML que acompanha o texto do e-mail: mesmo conteúdo, com o primeiro link
/// como botão na cor da marca. Tabela e estilo em linha, que é o que os
/// clientes de e-mail entendem. O texto puro segue junto para quem não abre HTML.
/// </summary>
public static partial class ModeloDeEmail
{
    private const string CorDaPlataforma = "#120c2b";

    public static string Html(string assunto, string texto, string? nome, string? cor)
    {
        var fundo = cor ?? CorDaPlataforma;
        var letra = ContrasteComBranco(fundo) >= 4.5 ? "#ffffff" : CorDaPlataforma;
        var corpo = new StringBuilder();
        var botaoUsado = false;
        foreach (var paragrafo in texto.Replace("\r\n", "\n", StringComparison.Ordinal).Trim().Split("\n\n"))
        {
            var linhas = paragrafo.Split('\n').Select(l => l.Trim()).ToArray();
            if (!botaoUsado && linhas is [var unica] && SoLink().IsMatch(unica))
            {
                botaoUsado = true;
                var href = WebUtility.HtmlEncode(unica);
                corpo.Append(CultureInfo.InvariantCulture,
                    $"<p style=\"margin:24px 0\"><a href=\"{href}\" style=\"display:inline-block;background:{fundo};color:{letra};text-decoration:none;font-weight:600;padding:12px 20px;border-radius:8px\">Abrir o link</a></p>");
                corpo.Append(CultureInfo.InvariantCulture,
                    $"<p style=\"margin:0 0 16px;font-size:12px;color:#4d4766;word-break:break-all\">{href}</p>");
                continue;
            }

            corpo.Append("<p style=\"margin:0 0 16px\">")
                .Append(string.Join("<br>", linhas.Select(ComLinks)))
                .Append("</p>");
        }

        var assinatura = WebUtility.HtmlEncode(nome ?? "Avalia Sales");
        return $"""
            <!doctype html>
            <html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>{WebUtility.HtmlEncode(assunto)}</title></head>
            <body style="margin:0;background:#f5f3fb;font:16px/1.5 -apple-system,'Segoe UI',Roboto,sans-serif;color:#120c2b">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td align="center" style="padding:24px 12px">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:520px;background:#ffffff;border-radius:12px;border-top:6px solid {fundo}">
            <tr><td style="padding:24px 24px 8px;font-weight:700;font-size:18px">{assinatura}</td></tr>
            <tr><td style="padding:8px 24px 16px">{corpo}</td></tr>
            </table>
            <p style="margin:16px 0 0;font-size:12px;color:#4d4766">Enviado pela plataforma Avalia Sales.</p>
            </td></tr></table>
            </body></html>
            """;
    }

    private static string ComLinks(string linha) =>
        Link().Replace(WebUtility.HtmlEncode(linha), m => $"<a href=\"{m.Value}\" style=\"color:#120c2b\">{m.Value}</a>");

    /// <summary>Razão de contraste WCAG entre a cor e o branco, para escolher a cor da letra do botão.</summary>
    private static double ContrasteComBranco(string hex)
    {
        static double Canal(string h) => int.Parse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0 is var c && c <= 0.03928
            ? c / 12.92
            : Math.Pow((c + 0.055) / 1.055, 2.4);
        var luz = 0.2126 * Canal(hex[1..3]) + 0.7152 * Canal(hex[3..5]) + 0.0722 * Canal(hex[5..7]);
        return 1.05 / (luz + 0.05);
    }

    [GeneratedRegex(@"^https?://\S+$")]
    private static partial Regex SoLink();

    [GeneratedRegex(@"https?://[^\s<]+")]
    private static partial Regex Link();
}
