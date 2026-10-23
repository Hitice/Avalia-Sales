using AvaliaSales.Domain;
using AvaliaSales.Domain.Pagamentos;

namespace AvaliaSales.Tests.Pagamentos;

public sealed class ParcelamentoTest
{
    [Theory]
    [InlineData(MeioDePagamento.Pix, 1, 89_900)]
    [InlineData(MeioDePagamento.Cartao, 1, 89_900)]
    [InlineData(MeioDePagamento.Cartao, 2, 97_899)]
    [InlineData(MeioDePagamento.Cartao, 12, 115_390)]
    public void Parcelado_o_cliente_paga_o_custo_e_a_vista_paga_o_valor(MeioDePagamento meio, int parcelas, long esperado) =>
        Assert.Equal(esperado, Parcelamento.Total(89_900, meio, parcelas));

    [Theory]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(12)]
    public void Total_menos_o_custo_volta_ao_valor_da_venda(int parcelas)
    {
        var total = Parcelamento.Total(89_900, MeioDePagamento.Cartao, parcelas);
        var liquido = total - (total * Parcelamento.Custo(MeioDePagamento.Cartao, parcelas) / 10_000);
        Assert.InRange(liquido, 89_900, 89_901);
    }

    [Theory]
    [InlineData(MeioDePagamento.Pix, 1, 71_136)]
    [InlineData(MeioDePagamento.Cartao, 1, 68_245)]
    [InlineData(MeioDePagamento.Cartao, 6, 71_920)]
    public void Avalia_paga_a_fatia_dela_no_custo_quando_o_cliente_nao_paga(MeioDePagamento meio, int parcelas, long esperado) =>
        Assert.Equal(esperado, Parcelamento.ParteLiquida(71_920, meio, parcelas));

    [Fact]
    public void Treze_vezes_e_recusado() =>
        Assert.Throws<RegraDeNegocioException>(() => Parcelamento.Total(10_000, MeioDePagamento.Cartao, 13));
}
