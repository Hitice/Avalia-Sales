using System.Globalization;

namespace AvaliaSales.Domain.Financeiro;

/// <summary>O mês de apuração no formato 2026-10, e a conta de meses entre dois deles.</summary>
public static class Competencias
{
    public static string Conferir(string competencia) =>
        DateOnly.TryParseExact((competencia ?? string.Empty).Trim() + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dia)
            ? dia.ToString("yyyy-MM", CultureInfo.InvariantCulture)
            : throw new RegraDeNegocioException("Mês inválido. Use o formato 2026-10.");

    public static string De(DateOnly data) => data.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>Meses corridos de <paramref name="de"/> até <paramref name="ate"/>; negativo se <paramref name="ate"/> vem antes.</summary>
    public static int Entre(string de, string ate) => Indice(ate) - Indice(de);

    public static string Somar(string competencia, int meses)
    {
        var indice = Indice(competencia) + meses;
        return $"{indice / 12:0000}-{indice % 12 + 1:00}";
    }

    private static int Indice(string competencia) =>
        int.Parse(competencia[..4], CultureInfo.InvariantCulture) * 12 + int.Parse(competencia[5..7], CultureInfo.InvariantCulture) - 1;
}
