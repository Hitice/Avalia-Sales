namespace AvaliaSales.Domain.Equipe;

/// <summary>
/// Comissão do vendedor sobre o valor bruto da venda, em pontos base
/// (2000 = 20%). Vale a mais específica: a do vendedor, senão a padrão do
/// revendedor. A venda grava o percentual do momento.
/// </summary>
public static class Comissao
{
    public const int Base = 10_000;

    /// <summary>Acima disso a comissão come a taxa da Avalia, a do Pagar.me e a margem.</summary>
    public const int MaximaBps = 5_000;

    public static int Conferir(int bps) =>
        bps is >= 0 and <= MaximaBps
            ? bps
            : throw new RegraDeNegocioException($"A comissão vai de 0% a {MaximaBps / 100}%.");

    /// <summary>Arredonda ao centavo mais próximo, como a divisão da cobrança.</summary>
    public static long Calcular(long valorCents, int bps) => checked((valorCents * bps + Base / 2) / Base);
}
