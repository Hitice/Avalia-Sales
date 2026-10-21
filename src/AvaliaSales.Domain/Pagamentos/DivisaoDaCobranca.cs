namespace AvaliaSales.Domain.Pagamentos;

public readonly record struct Divisao(long AvaliaCents, long RevendedorCents);

/// <summary>
/// Quanto de cada cobrança é da Avalia e quanto é do revendedor. A taxa vem em
/// pontos base (199 = 1,99%) e o resultado em centavos inteiros, arredondado
/// ao centavo mais próximo; o resto é todo do revendedor, para a soma fechar
/// com o valor cobrado.
/// </summary>
public static class DivisaoDaCobranca
{
    public const int Base = 10_000;

    public static Divisao Calcular(long valorCents, int taxaBps)
    {
        if (valorCents <= 0)
        {
            throw new RegraDeNegocioException("O valor da cobrança precisa ser maior que zero.");
        }

        if (taxaBps is < 0 or > Base)
        {
            throw new RegraDeNegocioException("A taxa da Avalia precisa ficar entre 0% e 100%.");
        }

        var avalia = checked((valorCents * taxaBps + Base / 2) / Base);
        return new Divisao(avalia, valorCents - avalia);
    }
}
