using AvaliaSales.Domain;
using AvaliaSales.Domain.Equipe;
using AvaliaSales.Domain.Pedidos;
using AvaliaSales.Domain.Vendas;

namespace AvaliaSales.Tests.Vendas;

public sealed class RegrasDaVendaTest
{
    [Theory]
    [InlineData(14_989)]
    [InlineData(0)]
    [InlineData(74_951)]
    public void Valor_abaixo_da_tabela_ou_acima_de_cinco_vezes_e_recusado(long valor) =>
        Assert.Throws<RegraDeNegocioException>(() => Venda.ConferirValor(valor, 14_990));

    [Theory]
    [InlineData(14_990)]
    [InlineData(74_950)]
    public void Valor_da_tabela_ate_cinco_vezes_passa(long valor) => Venda.ConferirValor(valor, 14_990);

    [Theory]
    [InlineData(14_990, 2_000, 2_998)]
    [InlineData(14_990, 2_500, 3_748)]
    [InlineData(10_001, 2_500, 2_500)]
    [InlineData(14_990, 0, 0)]
    public void Comissao_arredonda_ao_centavo_mais_proximo(long valor, int bps, long esperado) =>
        Assert.Equal(esperado, Comissao.Calcular(valor, bps));

    [Theory]
    [InlineData(-1)]
    [InlineData(5_001)]
    public void Comissao_fora_da_faixa_e_recusada(int bps) => Assert.Throws<RegraDeNegocioException>(() => Comissao.Conferir(bps));

    [Theory]
    [InlineData("11.222.333/0001-81")]
    [InlineData("52601815000120")]
    [InlineData("529.982.247-25")]
    public void Documento_valido_fica_so_com_digitos(string documento) =>
        Assert.Matches("^[0-9]{11,14}$", Documentos.Normalizar(documento, obrigatorio: true)!);

    [Theory]
    [InlineData("11.222.333/0001-82")]
    [InlineData("11111111111111")]
    [InlineData("123")]
    public void Documento_com_digito_errado_e_recusado(string documento) =>
        Assert.Throws<RegraDeNegocioException>(() => Documentos.Normalizar(documento, obrigatorio: true));

    [Theory]
    [InlineData("(11) 98765-4321", "11987654321")]
    [InlineData("+55 11 98765-4321", "5511987654321")]
    public void Telefone_fica_so_com_digitos(string telefone, string esperado) =>
        Assert.Equal(esperado, Telefones.Normalizar(telefone, obrigatorio: true));

    [Fact]
    public void Pedido_de_placas_so_anda_para_a_frente()
    {
        var pedido = new PedidoDePlacas(new Pacote(20, 28_000), DateTimeOffset.UtcNow);

        Assert.Throws<RegraDeNegocioException>(() => pedido.Despachar(Guid.NewGuid(), DateTimeOffset.UtcNow));
        pedido.RegistrarPagamento(DateTimeOffset.UtcNow);
        Assert.Throws<RegraDeNegocioException>(() => pedido.Cancelar());
        Assert.Throws<RegraDeNegocioException>(() => pedido.RegistrarPagamento(DateTimeOffset.UtcNow));
        Assert.Equal(1_400, pedido.CustoPorPlacaCents);
    }

    [Fact]
    public void Pacote_desativado_nao_vira_pedido()
    {
        var pacote = new Pacote(20, 28_000);
        pacote.DefinirAtivo(false);

        Assert.Throws<RegraDeNegocioException>(() => new PedidoDePlacas(pacote, DateTimeOffset.UtcNow));
    }
}
