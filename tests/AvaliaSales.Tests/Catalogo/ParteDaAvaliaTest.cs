using AvaliaSales.Domain;
using AvaliaSales.Domain.Catalogo;
using AvaliaSales.Domain.Pagamentos;

namespace AvaliaSales.Tests.Catalogo;

public sealed class ParteDaAvaliaTest
{
    private static ProdutoDaPlataforma Produto(TipoDeProduto tipo, long sugeridoCents, int descontoBps)
    {
        var produto = new ProdutoDaPlataforma("teste", "Teste", "", tipo, sugeridoCents, 1, DateTimeOffset.UtcNow);
        produto.DefinirDescontoDaRevenda(descontoBps);
        return produto;
    }

    [Theory]
    [InlineData(TipoDeProduto.Servico, 89_900, 2_000, 71_920)]
    [InlineData(TipoDeProduto.PlacaComServico, 49_900, 1_500, 42_415)]
    [InlineData(TipoDeProduto.Servico, 3_990, 2_000, 3_192)]
    [InlineData(TipoDeProduto.Placa, 9_990, 2_000, 0)]
    public void Parte_da_Avalia_e_o_sugerido_menos_o_desconto_da_revenda(TipoDeProduto tipo, long sugeridoCents, int descontoBps, long esperado) =>
        Assert.Equal(esperado, Produto(tipo, sugeridoCents, descontoBps).ParteDaAvaliaCents);

    [Theory]
    [InlineData(-1)]
    [InlineData(10_001)]
    public void Desconto_fora_de_0_a_100_e_recusado(int descontoBps) =>
        Assert.Throws<RegraDeNegocioException>(() => Produto(TipoDeProduto.Servico, 10_000, descontoBps));

    [Fact]
    public void Cobranca_soma_a_taxa_e_a_parte_e_nao_passa_do_valor()
    {
        Assert.Equal(298 + 8_000, new Cobranca(OrigemDaCobranca.Venda, null, MeioDePagamento.Pix, 14_990, 199, 8_000).TaxaAvaliaCents);
        Assert.Throws<RegraDeNegocioException>(() => new Cobranca(OrigemDaCobranca.Venda, null, MeioDePagamento.Pix, 10_000, 0, 10_001));
    }
}
