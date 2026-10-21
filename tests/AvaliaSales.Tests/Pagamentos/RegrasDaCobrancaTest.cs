using AvaliaSales.Domain;
using AvaliaSales.Domain.Pagamentos;

namespace AvaliaSales.Tests.Pagamentos;

public sealed class RegrasDaCobrancaTest
{
    [Theory]
    [InlineData(10_000, 199, 199, 9_801)]
    [InlineData(14_990, 199, 298, 14_692)]
    [InlineData(100, 199, 2, 98)]
    [InlineData(1, 199, 0, 1)]
    [InlineData(5_000, 0, 0, 5_000)]
    public void Taxa_da_avalia_arredonda_ao_centavo_e_o_resto_e_do_revendedor(long valor, int taxaBps, long avalia, long revendedor)
    {
        var divisao = DivisaoDaCobranca.Calcular(valor, taxaBps);

        Assert.Equal(avalia, divisao.AvaliaCents);
        Assert.Equal(revendedor, divisao.RevendedorCents);
        Assert.Equal(valor, divisao.AvaliaCents + divisao.RevendedorCents);
    }

    [Theory]
    [InlineData(0, 199)]
    [InlineData(-10, 199)]
    [InlineData(1_000, -1)]
    [InlineData(1_000, 10_001)]
    public void Valor_ou_taxa_fora_do_intervalo_e_recusado(long valor, int taxaBps) =>
        Assert.Throws<RegraDeNegocioException>(() => DivisaoDaCobranca.Calcular(valor, taxaBps));

    [Fact]
    public void Cobranca_paga_nao_volta_a_pendente_nem_a_cancelada()
    {
        var cobranca = new Cobranca(OrigemDaCobranca.Venda, null, MeioDePagamento.Pix, 10_000, 199);

        Assert.True(cobranca.Atualizar(SituacaoDaCobranca.Paga, DateTimeOffset.UtcNow));
        Assert.False(cobranca.Atualizar(SituacaoDaCobranca.Pendente, null));
        Assert.False(cobranca.Atualizar(SituacaoDaCobranca.Cancelada, null));
        Assert.Equal(SituacaoDaCobranca.Paga, cobranca.Situacao);
        Assert.True(cobranca.Atualizar(SituacaoDaCobranca.Estornada, null));
    }

    [Fact]
    public void Taxa_fica_gravada_na_cobranca_no_momento_da_criacao()
    {
        var cobranca = new Cobranca(OrigemDaCobranca.Venda, null, MeioDePagamento.Cartao, 14_990, 199);

        Assert.Equal(298, cobranca.TaxaAvaliaCents);
        Assert.Equal(32, cobranca.Codigo.Length);
    }
}
