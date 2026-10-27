using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace AvaliaSales.Infrastructure.Integracoes.Resend;

/// <summary>
/// O HTML que acompanha o texto do e-mail, montado do próprio texto e dos destaques que o servidor marcou
/// (<see cref="Application.Avisos.DestaquesDoEmail"/>):
/// - linha "Rótulo: https://..." com endereço marcado vira botão com esse rótulo, na cor da marca;
/// - linha "Rótulo: 123456" com código marcado mostra o código em destaque;
/// - o resto vira parágrafo; só endereço marcado é clicável, e o que veio do usuário fica como texto.
/// Assim o texto puro continua legível e quem escreve o e-mail não mexe em HTML.
/// Tabela e estilo em linha, que é o que os clientes de e-mail entendem. O logo
/// vai em PNG: Gmail e Outlook não mostram SVG.
/// </summary>
public static partial class ModeloDeEmail
{
    private const string CorDaPlataforma = "#120c2b";
    private const string CorDoTexto = "#3a3550";
    private const string CorSecundaria = "#8a8599";
    private const string Fonte = "-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif";

    /// <summary>O traço da marca (--traco do front), com a cor do meio para quem não desenha degradê.</summary>
    private const string Traco = "background-color:#a646e0;background-image:linear-gradient(90deg,#ee46bc 0%,#a646e0 38%,#7a3cf0 58%,#465fff 100%)";

    /// <param name="assunto">Assunto, que vai no título do HTML.</param>
    /// <param name="texto">O texto puro do e-mail, de onde sai o corpo.</param>
    /// <param name="nome">Nome da revenda que envia; nulo é e-mail da plataforma, com o logo.</param>
    /// <param name="cor">Cor da marca da revenda em #rrggbb; nula usa a da plataforma.</param>
    /// <param name="urlDoLogo">PNG do logo da plataforma em endereço absoluto; nulo escreve o nome.</param>
    /// <param name="destaques">Os hashes dos endereços e códigos que o servidor marcou; sem eles, nada vira botão nem link.</param>
    /// <param name="urlDoPainel">Endereço da plataforma, que o rodapé mostra; nulo deixa o rodapé sem ele.</param>
    public static string Html(string assunto, string texto, string? nome, string? cor, string? urlDoLogo = null, IReadOnlySet<string>? destaques = null, string? urlDoPainel = null)
    {
        destaques ??= new HashSet<string>();
        bool Marcado(string valor) => destaques.Contains(Application.Avisos.DestaquesDoEmail.Hash(valor));
        var fundo = cor ?? CorDaPlataforma;
        var letra = ContrasteComBranco(fundo) >= 4.5 ? "#ffffff" : CorDaPlataforma;
        var corpo = new StringBuilder();
        foreach (var paragrafo in texto.Replace("\r\n", "\n", StringComparison.Ordinal).Trim().Split("\n\n"))
        {
            var comuns = new List<string>();
            foreach (var linha in paragrafo.Split('\n').Select(l => l.Trim()))
            {
                if (Botao().Match(linha) is { Success: true } botao && Marcado(botao.Groups["url"].Value))
                {
                    Paragrafo(corpo, comuns, Marcado);
                    var href = WebUtility.HtmlEncode(botao.Groups["url"].Value);
                    var rotulo = botao.Groups["rotulo"].Success ? WebUtility.HtmlEncode(botao.Groups["rotulo"].Value.Trim()) : "Abrir";
                    corpo.Append(CultureInfo.InvariantCulture,
                        $"<p style=\"margin:4px 0 20px\"><a href=\"{href}\" style=\"display:inline-block;background:{fundo};color:{letra};text-decoration:none;font-size:14px;font-weight:500;line-height:20px;padding:10px 20px;border-radius:6px\">{rotulo}</a></p>");
                }
                else if (Codigo().Match(linha) is { Success: true } codigo && Marcado(codigo.Groups["codigo"].Value))
                {
                    Paragrafo(corpo, comuns, Marcado);
                    corpo.Append(CultureInfo.InvariantCulture,
                        $"<p style=\"margin:0 0 8px\">{WebUtility.HtmlEncode(codigo.Groups["rotulo"].Value.Trim())}</p>");
                    corpo.Append(CultureInfo.InvariantCulture,
                        $"<p style=\"margin:0 0 24px\"><span style=\"display:inline-block;font:600 24px/1 ui-monospace,Menlo,Consolas,monospace;letter-spacing:5px;color:{CorDaPlataforma};padding:12px 18px;border-radius:6px;background:#f7f6fa;border:1px solid #e8e6ef\">{codigo.Groups["codigo"].Value}</span></p>");
                }
                else
                {
                    comuns.Add(linha);
                }
            }

            Paragrafo(corpo, comuns, Marcado);
        }

        var topo = nome is null && urlDoLogo is not null
            ? $"<img src=\"{WebUtility.HtmlEncode(urlDoLogo)}\" width=\"96\" alt=\"Avalia Sales\" style=\"display:block;width:96px;max-width:100%;height:auto;border:0\">"
            : $"<span style=\"font-size:15px;font-weight:600;color:{CorDaPlataforma}\">{WebUtility.HtmlEncode(nome ?? "Avalia Sales")}</span>";
        // A resposta ao e-mail da revenda vai ao dono dela (ClienteResend); só o da plataforma é automático.
        var origem = nome is null ? "Mensagem automática da Avalia Sales." : $"Enviado por {WebUtility.HtmlEncode(nome)} pela plataforma Avalia Sales.";
        var site = Uri.TryCreate(urlDoPainel, UriKind.Absolute, out var url)
            ? $"<br><a href=\"{WebUtility.HtmlEncode(url.GetLeftPart(UriPartial.Authority))}\" style=\"color:{CorSecundaria};text-decoration:underline\">{WebUtility.HtmlEncode(url.Host)}</a>"
            : string.Empty;
        return $"""
            <!doctype html>
            <html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width"><meta name="color-scheme" content="light"><title>{WebUtility.HtmlEncode(assunto)}</title></head>
            <body style="margin:0;background:#f6f6f8;font-family:{Fonte};font-size:15px;line-height:24px;font-weight:400;color:{CorDoTexto};-webkit-font-smoothing:antialiased">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f6f6f8"><tr><td align="center" style="padding:32px 12px">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;background:#ffffff;border:1px solid #ebe9f0;border-radius:10px;overflow:hidden">
            <tr><td style="height:3px;line-height:3px;font-size:0;{(cor is null ? Traco : $"background-color:{cor}")}">&nbsp;</td></tr>
            <tr><td style="padding:28px 28px 4px">{topo}</td></tr>
            <tr><td style="padding:20px 28px 12px">{corpo}</td></tr>
            </table>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px"><tr><td style="padding:20px 28px 0;font-size:12px;line-height:18px;color:{CorSecundaria}">
            {origem}{site}
            </td></tr></table>
            </td></tr></table>
            </body></html>
            """;
    }

    private static void Paragrafo(StringBuilder corpo, List<string> linhas, Func<string, bool> marcado)
    {
        if (linhas.Count == 0)
        {
            return;
        }

        corpo.Append("<p style=\"margin:0 0 16px\">")
            .Append(string.Join("<br>", linhas.Select(l => ComLinks(l, marcado))))
            .Append("</p>");
        linhas.Clear();
    }

    /// <summary>Só o endereço marcado pelo servidor vira link; o resto é escapado e fica como texto.</summary>
    private static string ComLinks(string linha, Func<string, bool> marcado) =>
        string.Concat(Pedacos(linha, marcado));

    private static IEnumerable<string> Pedacos(string linha, Func<string, bool> marcado)
    {
        var inicio = 0;
        foreach (Match link in Link().Matches(linha))
        {
            if (!marcado(link.Value))
            {
                continue;
            }

            yield return WebUtility.HtmlEncode(linha[inicio..link.Index]);
            var href = WebUtility.HtmlEncode(link.Value);
            yield return $"<a href=\"{href}\" style=\"color:{CorDaPlataforma}\">{href}</a>";
            inicio = link.Index + link.Length;
        }

        yield return WebUtility.HtmlEncode(linha[inicio..]);
    }

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

    [GeneratedRegex(@"https?://\S+")]
    private static partial Regex Link();
}
