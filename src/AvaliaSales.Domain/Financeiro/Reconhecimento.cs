namespace AvaliaSales.Domain.Financeiro;

/// <summary>Uma linha do razão vista pela apropriação: o valor, o mês, em quantos meses entra no resultado e a linha que ela estorna.</summary>
public sealed record LinhaApropriada(Guid Id, long ValorCents, string Competencia, int Meses, Guid? EstornaId);

/// <summary>
/// Quanto de cada linha entra no resultado de cada mês. A placa vendida é uma
/// obrigação de 12 meses (o QR passa pela plataforma, CPC 47): a receita entra
/// em doze partes a partir do mês do pagamento, e o custo da placa acompanha.
/// As partes somam o valor exato, sem ponto flutuante. O estorno para a
/// apropriação no mês dele e devolve ali o que já tinha entrado: mês anterior
/// não muda.
/// </summary>
public static class Reconhecimento
{
    public const int MesesDaPlaca = Placas.Placa.ValidadeMeses;

    public static long NoMes(IEnumerable<LinhaApropriada> linhas, string mes)
    {
        var (originais, estornos) = Separar(linhas);
        long total = 0;
        foreach (var linha in originais)
        {
            var estornadaEm = estornos.GetValueOrDefault(linha.Id)?.Competencia;
            var indice = Competencias.Entre(linha.Competencia, mes);
            if (indice >= 0 && indice < linha.Meses && (estornadaEm is null || Competencias.Entre(estornadaEm, mes) < 0))
            {
                total += Acumulado(linha, indice + 1) - Acumulado(linha, indice);
            }

            if (estornadaEm == mes)
            {
                total -= Acumulado(linha, Math.Clamp(Competencias.Entre(linha.Competencia, mes), 0, linha.Meses));
            }
        }

        return total;
    }

    /// <summary>O que já entrou no caixa e ainda não entrou no resultado, no fim do mês: o passivo de contrato.</summary>
    public static long AReconhecer(IEnumerable<LinhaApropriada> linhas, string mes)
    {
        var (originais, estornos) = Separar(linhas);
        long total = 0;
        foreach (var linha in originais.Where(l => Competencias.Entre(l.Competencia, mes) >= 0))
        {
            var estornadaEm = estornos.GetValueOrDefault(linha.Id)?.Competencia;
            if (estornadaEm is null || Competencias.Entre(estornadaEm, mes) < 0)
            {
                total += linha.ValorCents - Acumulado(linha, Math.Clamp(Competencias.Entre(linha.Competencia, mes) + 1, 0, linha.Meses));
            }
        }

        return total;
    }

    /// <summary>A soma das <paramref name="partes"/> primeiras partes, com o sinal do valor.</summary>
    private static long Acumulado(LinhaApropriada linha, int partes) =>
        Math.Sign(linha.ValorCents) * (Math.Abs(linha.ValorCents) * partes / Math.Max(linha.Meses, 1));

    private static (List<LinhaApropriada> Originais, Dictionary<Guid, LinhaApropriada> Estornos) Separar(IEnumerable<LinhaApropriada> linhas)
    {
        var todas = linhas.ToList();
        return (todas.Where(l => l.EstornaId is null).ToList(),
            todas.Where(l => l.EstornaId is not null).GroupBy(l => l.EstornaId!.Value).ToDictionary(g => g.Key, g => g.First()));
    }
}
