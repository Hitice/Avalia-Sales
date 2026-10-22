using System.Globalization;

namespace AvaliaSales.Domain.Revendedores;

/// <summary>Contraste entre duas cores pela luminância relativa da WCAG 2.2. A tela faz a mesma conta antes de salvar.</summary>
public static class Contraste
{
    public const string Branco = "#FFFFFF";

    /// <summary>A noite da marca da plataforma: o texto escuro sobre cor clara.</summary>
    public const string Escuro = "#120C2B";

    public static string Normalizar(string? cor)
    {
        var limpo = (cor ?? string.Empty).Trim().TrimStart('#').ToUpperInvariant();
        return limpo.Length == 6 && limpo.All(char.IsAsciiHexDigit)
            ? "#" + limpo
            : throw new RegraDeNegocioException("Use a cor em hexadecimal, como #120C2B.");
    }

    public static double Razao(string a, string b)
    {
        var (claro, escuro) = (Luminancia(a), Luminancia(b)) is var (x, y) && x > y ? (x, y) : (Luminancia(b), Luminancia(a));
        return (claro + 0.05) / (escuro + 0.05);
    }

    /// <summary>O texto que fica sobre a cor: branco ou escuro, o de maior contraste.</summary>
    public static string TextoSobre(string cor) => Razao(cor, Branco) >= Razao(cor, Escuro) ? Branco : Escuro;

    private static double Luminancia(string cor)
    {
        double Canal(int inicio)
        {
            var c = int.Parse(cor.AsSpan(inicio, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Canal(1) + 0.7152 * Canal(3) + 0.0722 * Canal(5);
    }
}
