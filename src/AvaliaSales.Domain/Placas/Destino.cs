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

        // Quebra de linha e afins não entram no cabeçalho Location: o Kestrel recusa, e a placa impressa passaria a responder 500.
        if (destino.Any(char.IsControl))
        {
            throw new RegraDeNegocioException("O endereço tem quebra de linha ou tabulação. Cole o link de novo.");
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

        var gravavel = ParaCabecalho(destino) ?? throw new RegraDeNegocioException("O endereço tem caractere que a placa não aceita. Confira o link.");
        // A coluna tem o mesmo limite, e o %XX do acento engorda o endereço.
        return gravavel.Length <= TamanhoMaximo ? gravavel : throw new RegraDeNegocioException("O endereço é longo demais para uma placa.");
    }

    /// <summary>
    /// O endereço como o cabeçalho Location aceita: só ASCII e sem caractere de controle. Acento no
    /// caminho ("/são-joão") ou no domínio vira %XX e punycode, porque o Kestrel recusa o byte fora do
    /// ASCII e a placa impressa daria 500. Destino já em ASCII fica como foi digitado. Nulo quando o
    /// texto não é um endereço absoluto utilizável.
    /// </summary>
    public static string? ParaCabecalho(string destino)
    {
        if (destino.Any(char.IsControl))
        {
            return null;
        }

        if (destino.All(char.IsAscii))
        {
            return destino;
        }

        return Uri.TryCreate(destino, UriKind.Absolute, out var uri) && uri.AbsoluteUri.All(char.IsAscii) ? uri.AbsoluteUri : null;
    }

    [GeneratedRegex("^[a-z][a-z0-9+.-]*:", RegexOptions.IgnoreCase)]
    private static partial Regex ComEsquema();
}
