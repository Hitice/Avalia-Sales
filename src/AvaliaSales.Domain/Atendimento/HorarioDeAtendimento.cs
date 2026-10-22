namespace AvaliaSales.Domain.Atendimento;

/// <summary>
/// O atendimento da Avalia: segunda a sexta, das 8h às 18h de Brasília, sem
/// feriado nacional. O prazo de resposta é de 24 horas contadas só em dia útil:
/// a mensagem que chega fora do expediente começa a contar na abertura seguinte,
/// e sábado, domingo e feriado não contam.
/// </summary>
public static class HorarioDeAtendimento
{
    public const int Abertura = 8;
    public const int Fechamento = 18;
    public static readonly TimeSpan Prazo = TimeSpan.FromHours(24);

    private static readonly TimeZoneInfo Brasilia = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public static DateTimeOffset PrazoDaResposta(DateTimeOffset recebidaEm)
    {
        var cursor = Inicio(TimeZoneInfo.ConvertTime(recebidaEm, Brasilia).DateTime);
        var restante = Prazo;
        while (true)
        {
            if (DiaUtil(cursor))
            {
                var fimDoDia = cursor.Date.AddDays(1);
                var disponivel = fimDoDia - cursor;
                if (restante <= disponivel)
                {
                    return ParaUtc(cursor + restante);
                }

                restante -= disponivel;
                cursor = fimDoDia;
            }
            else
            {
                cursor = cursor.Date.AddDays(1);
            }
        }
    }

    /// <summary>Dentro do expediente, conta já; antes da abertura, às 8h; depois do fechamento ou em dia sem atendimento, na próxima abertura.</summary>
    private static DateTime Inicio(DateTime local)
    {
        if (DiaUtil(local) && local.Hour < Abertura)
        {
            return local.Date.AddHours(Abertura);
        }

        if (DiaUtil(local) && local.Hour < Fechamento)
        {
            return local;
        }

        var dia = local.Date.AddDays(1);
        while (!DiaUtil(dia))
        {
            dia = dia.AddDays(1);
        }

        return dia.AddHours(Abertura);
    }

    private static DateTimeOffset ParaUtc(DateTime local)
    {
        var semFuso = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(semFuso, Brasilia.GetUtcOffset(semFuso)).ToUniversalTime();
    }

    public static bool DiaUtil(DateTime dia) =>
        dia.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !Feriado(dia.Date);

    /// <summary>Feriados nacionais (Lei 662/1949, 6.802/1980 e 14.759/2023) e a Sexta-feira Santa.</summary>
    public static bool Feriado(DateTime dia) =>
        (dia.Month, dia.Day) is (1, 1) or (4, 21) or (5, 1) or (9, 7) or (10, 12) or (11, 2) or (11, 15) or (11, 20) or (12, 25)
        || dia == Pascoa(dia.Year).AddDays(-2);

    /// <summary>Domingo de Páscoa pelo algoritmo de Meeus/Jones/Butcher (calendário gregoriano).</summary>
    public static DateTime Pascoa(int ano)
    {
        var a = ano % 19;
        var b = ano / 100;
        var c = ano % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var mes = (h + l - 7 * m + 114) / 31;
        var dia = (h + l - 7 * m + 114) % 31 + 1;
        return new DateTime(ano, mes, dia);
    }
}
