using System.Text.RegularExpressions;

namespace AvaliaSales.Domain.Placas;

/// <summary>
/// O endereço que abre direto o formulário de avaliação do Google. A forma
/// writereview aceita o Place ID e funciona no celular e no computador; o
/// g.page/r/... usa o CID, que só o dono do perfil vê, e entra quando o
/// vendedor cola o link pronto do Perfil da Empresa.
/// </summary>
public static partial class LinkDeAvaliacao
{
    private static readonly HashSet<string> HostsDoGoogle = new(StringComparer.OrdinalIgnoreCase)
    {
        "g.page", "search.google.com", "www.google.com", "google.com", "maps.google.com",
        "maps.app.goo.gl", "goo.gl", "www.google.com.br", "google.com.br",
    };

    public static string De(string lugarNoGoogle) =>
        PareceValido(lugarNoGoogle)
            ? "https://search.google.com/local/writereview?placeid=" + Uri.EscapeDataString(lugarNoGoogle.Trim())
            : throw new RegraDeNegocioException("Lugar do Google inválido. Busque o negócio de novo.");

    /// <summary>
    /// O link colado, se for do Google. Com Place ID na URL sai o endereço
    /// canônico; sem ele, o colado. Fora dos domínios do Google, recusa: a
    /// placa de avaliação não pode levar a outro lugar.
    /// </summary>
    public static string Colado(string? texto)
    {
        var url = Destino.Normalizar(texto);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp
            || !HostsDoGoogle.Contains(uri.Host))
        {
            throw new RegraDeNegocioException("Link de avaliação do Google inválido. Copie o link do Perfil da Empresa (g.page/r/...).");
        }

        var lugar = System.Web.HttpUtility.ParseQueryString(uri.Query) is var consulta
            ? consulta["placeid"] ?? consulta["place_id"]
            : null;
        return lugar is not null && PareceValido(lugar) ? De(lugar) : "https://" + uri.Host + uri.PathAndQuery;
    }

    /// <summary>Confere forma, e não existência: o formato do Place ID é opaco e pode mudar.</summary>
    public static bool PareceValido(string? lugar) =>
        lugar is not null && lugar.Trim().Length is >= 10 and <= 255 && FormaDoLugar().IsMatch(lugar.Trim());

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex FormaDoLugar();
}
