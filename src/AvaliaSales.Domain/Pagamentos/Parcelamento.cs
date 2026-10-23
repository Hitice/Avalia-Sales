namespace AvaliaSales.Domain.Pagamentos;

/// <summary>
/// Custo do meio de pagamento nas condições acordadas com o Pagar.me, em
/// pontos base: Pix 1,09%; cartão pelo custo efetivo total com recebimento em
/// 2 dias, de 1x a 12x. Os custos fixos por transação (gateway e antifraude)
/// ficam com a revenda e não entram aqui.
/// </summary>
public static class Parcelamento
{
    public const int Maximo = 12;
    public const int PixBps = 109;
    private const int Base = 10_000;

    private static readonly int[] CartaoBps = [511, 817, 947, 1_077, 1_207, 1_338, 1_566, 1_695, 1_823, 1_952, 2_081, 2_209];

    /// <summary>O custo do cartão de 1x a 12x, na ordem: o primeiro é o à vista.</summary>
    public static IReadOnlyList<int> CustoDoCartaoBps => CartaoBps;

    public static int Custo(MeioDePagamento meio, int parcelas) => meio switch
    {
        MeioDePagamento.Pix => PixBps,
        MeioDePagamento.Cartao => CartaoBps[Conferir(parcelas) - 1],
        _ => 0,
    };

    /// <summary>
    /// Parcelado, o cliente paga o custo: o total, menos o custo, volta ao
    /// valor da venda. À vista e no Pix, o total é o valor.
    /// </summary>
    public static long Total(long valorCents, MeioDePagamento meio, int parcelas)
    {
        if (!ClientePagaOCusto(meio, parcelas))
        {
            return valorCents;
        }

        var custo = CartaoBps[parcelas - 1];
        return checked((valorCents * Base + (Base - custo) - 1) / (Base - custo));
    }

    /// <summary>
    /// A parte da Avalia já com a fatia dela no custo, quando o cliente não paga
    /// o custo (Pix e à vista): a revenda paga a tarifa do pedido inteiro no
    /// split, e a Avalia devolve a dela recebendo menos.
    /// </summary>
    public static long ParteLiquida(long parteCents, MeioDePagamento meio, int parcelas) =>
        ClientePagaOCusto(meio, parcelas)
            ? parteCents
            : parteCents - checked((parteCents * Custo(meio, parcelas) + Base / 2) / Base);

    public static bool ClientePagaOCusto(MeioDePagamento meio, int parcelas) => meio == MeioDePagamento.Cartao && Conferir(parcelas) > 1;

    private static int Conferir(int parcelas) =>
        parcelas is >= 1 and <= Maximo ? parcelas : throw new RegraDeNegocioException($"O parcelamento vai de 1 a {Maximo} vezes.");
}
