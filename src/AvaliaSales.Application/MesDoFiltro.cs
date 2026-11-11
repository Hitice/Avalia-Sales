namespace AvaliaSales.Application;

/// <summary>
/// O filtro de mês das listas: o mês de Brasília (2026-10) vira o intervalo em UTC,
/// do primeiro instante do mês ao primeiro do mês seguinte, este fora.
/// </summary>
public static class MesDoFiltro
{
    private static readonly TimeZoneInfo Brasilia = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    /// <summary>Nulo sem mês; mês fora do formato recusa com a mensagem de <see cref="Domain.Financeiro.Competencias.Conferir"/>.</summary>
    public static (DateTimeOffset Inicio, DateTimeOffset Fim)? Intervalo(string? mes)
    {
        if (string.IsNullOrWhiteSpace(mes))
        {
            return null;
        }

        var conferido = Domain.Financeiro.Competencias.Conferir(mes);
        return (Inicio(conferido), Inicio(Domain.Financeiro.Competencias.Somar(conferido, 1)));
    }

    private static DateTimeOffset Inicio(string competencia)
    {
        var dia = DateOnly.ParseExact(competencia + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(dia.ToDateTime(TimeOnly.MinValue), Brasilia), TimeSpan.Zero);
    }
}
