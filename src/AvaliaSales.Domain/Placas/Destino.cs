using System.Text.RegularExpressions;

namespace AvaliaSales.Domain.Placas;

/// <summary>
/// Para onde a placa manda quem a lê. É o único campo que leva um desconhecido
/// para fora da plataforma sem clique intermediário, por isso é conferido aqui
/// e não só pelo formulário.
/// </summary>
public static partial class Destino
{
    public const int TamanhoMaximo = 1000;

    /// <summary>Completa o https:// que quase ninguém digita.</summary>
    public static string Normalizar(string? entrada)
    {
        var limpo = (entrada ?? string.Empty).Trim();
        if (limpo.Length == 0 || ComEsquema().IsMatch(limpo))
        {
            return limpo;
        }

        return "https://" + limpo.TrimStart('/');
    }

    /// <summary>
    /// O destino pronto para gravar, ou a recusa com o que corrigir.
    /// <paramref name="hostDaPlataforma"/> barra a placa que aponta para outra
    /// placa: o navegador entraria em laço.
    /// </summary>
    public static string Conferir(string? entrada, string hostDaPlataforma)
    {
        var destino = Normalizar(entrada);
        if (destino.Length == 0)
        {
            throw new RegraDeNegocioException("Informe o endereço de destino.");
        }

        if (destino.Length > TamanhoMaximo)
        {
            throw new RegraDeNegocioException("O endereço é longo demais para uma placa.");
        }

        // javascript:, data: e afins: o navegador de quem lê executaria o que estivesse ali.
        if (!Uri.TryCreate(destino, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new RegraDeNegocioException("O destino precisa começar com http:// ou https://.");
        }

        if (!uri.Host.Contains('.', StringComparison.Ordinal))
        {
            throw new RegraDeNegocioException("O endereço não tem um domínio válido.");
        }

        if (string.Equals(uri.Host, hostDaPlataforma, StringComparison.OrdinalIgnoreCase)
            && (uri.AbsolutePath.StartsWith("/q/", StringComparison.OrdinalIgnoreCase) || uri.AbsolutePath.StartsWith("/l/", StringComparison.OrdinalIgnoreCase)))
        {
            throw new RegraDeNegocioException("O destino não pode ser o endereço de outra placa ou de outro link.");
        }

        return destino;
    }

    [GeneratedRegex("^[a-z][a-z0-9+.-]*:", RegexOptions.IgnoreCase)]
    private static partial Regex ComEsquema();
}
