using AvaliaSales.Domain;
using AvaliaSales.Domain.Pagamentos;

namespace AvaliaSales.Tests.Pagamentos;

public sealed class RegrasDaCobrancaTest
{
    [Theory]
    [InlineData(14_990, 0, 0, 14_990)]
    [InlineData(89_900, 71_137, 71_137, 18_763)]
    [InlineData(90_000, 90_000, 90_000, 0)]
    public void Split_manda_a_parte_da_avalia_em_centavos_e_o_resto_e_do_revendedor(long valor, long parte, long avalia, long revendedor)
    {
        var divisao = new Cobranca(OrigemDaCobranca.Venda, null, MeioDePagamento.Pix, valor, parte).Divisao;

        Assert.Equal(avalia, divisao.AvaliaCents);
        Assert.Equal(revendedor, divisao.RevendedorCents);
        Assert.Equal(valor, divisao.AvaliaCents + divisao.RevendedorCents);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-10, 0)]
    [InlineData(1_000, -1)]
    [InlineData(1_000, 1_001)]
    public void Valor_zerado_ou_parte_fora_do_valor_e_recusado(long valor, long parte) =>
        Assert.Throws<RegraDeNegocioException>(() => new Cobranca(OrigemDaCobranca.Venda, null, MeioDePagamento.Pix, valor, parte));

    [Fact]
    public void Cobranca_paga_nao_volta_a_pendente_nem_a_cancelada()
    {
        var cobranca = new Cobranca(OrigemDaCobranca.Venda, null, MeioDePagamento.Pix, 10_000);

        Assert.True(cobranca.Atualizar(SituacaoDaCobranca.Paga, DateTimeOffset.UtcNow));
        Assert.False(cobranca.Atualizar(SituacaoDaCobranca.Pendente, null));
        Assert.False(cobranca.Atualizar(SituacaoDaCobranca.Cancelada, null));
        Assert.Equal(SituacaoDaCobranca.Paga, cobranca.Situacao);
        Assert.True(cobranca.Atualizar(SituacaoDaCobranca.Estornada, null));
    }

    [Fact]
    public void Venda_de_placa_nao_manda_nada_para_a_avalia_no_split()
    {
        var cobranca = new Cobranca(OrigemDaCobranca.Venda, null, MeioDePagamento.Cartao, 14_990);

        Assert.Equal(0, cobranca.ParteDaAvaliaCents);
        Assert.Equal(new Divisao(0, 14_990), cobranca.Divisao);
        Assert.Equal(32, cobranca.Codigo.Length);
    }
}
