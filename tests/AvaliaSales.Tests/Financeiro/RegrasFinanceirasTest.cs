using AvaliaSales.Domain;
using AvaliaSales.Domain.Financeiro;
using AvaliaSales.Domain.Pedidos;
using AvaliaSales.Domain.Placas;

namespace AvaliaSales.Tests.Financeiro;

public sealed class CupomTest
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Percentual_arredonda_ao_centavo_e_fixo_nunca_passa_do_valor()
    {
        Assert.Equal(4_200, new Cupom("dez", 1_500, null, null, null).Aplicar(28_000, Agora));
        Assert.Equal(28_000, new Cupom("tudo", null, 99_999, null, null).Aplicar(28_000, Agora));
    }

    [Fact]
    public void Cupom_vencido_esgotado_ou_desligado_e_recusado()
    {
        var vencido = new Cupom("velho", 1_000, null, Agora.AddDays(-1), null);
        var esgotado = new Cupom("unico", 1_000, null, null, 1);
        esgotado.Aplicar(10_000, Agora);
        var desligado = new Cupom("pausa", 1_000, null, null, null);
        desligado.DefinirAtivo(false);

        Assert.Throws<RegraDeNegocioException>(() => vencido.Aplicar(10_000, Agora));
        Assert.Throws<RegraDeNegocioException>(() => esgotado.Aplicar(10_000, Agora));
        Assert.Throws<RegraDeNegocioException>(() => desligado.Aplicar(10_000, Agora));
    }

    [Fact]
    public void Desconto_e_um_dos_dois_percentual_ou_reais() =>
        Assert.Throws<RegraDeNegocioException>(() => new Cupom("ambos", 1_000, 500, null, null));

    [Fact]
    public void Codigo_fica_em_maiusculas_sem_espaco() => Assert.Equal("BEMVINDO10", new Cupom(" bem-vindo 10 ", 1_000, null, null, null).Codigo);

    [Fact]
    public void Pedido_com_cupom_guarda_o_preco_o_desconto_e_o_custo_pago_por_placa()
    {
        var pedido = new PedidoDePlacas(new Pacote(20, 28_000), Agora, new Cupom("dez", 1_000, null, null, null));

        Assert.Equal(28_000, pedido.PrecoDoPacoteCents);
        Assert.Equal(2_800, pedido.DescontoCents);
        Assert.Equal(25_200, pedido.ValorCents);
        Assert.Equal(1_260, pedido.CustoPorPlacaCents);
        Assert.Equal("DEZ", pedido.Cupom);
    }
}

public sealed class RenovacaoTest
{
    private static readonly DateTimeOffset Venda = new(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);

    private static Placa Vendida()
    {
        var placa = new Placa(new PlacaEmitida("K7M2PX", 1, Guid.NewGuid()), 1_400, Venda);
        placa.Vender(Guid.NewGuid(), "https://padaria.com.br", Venda);
        return placa;
    }

    [Fact]
    public void Custa_sessenta_por_cento_e_conta_do_vencimento_antigo_mesmo_paga_atrasada()
    {
        var placa = Vendida();
        var renovacao = new Renovacao(placa, 14_990, 6_000, Venda.AddMonths(12).AddDays(15));

        Assert.True(renovacao.Confirmar(placa, Venda.AddMonths(12).AddDays(15)));
        Assert.Equal(8_994, renovacao.ValorCents);
        Assert.Equal(Venda.AddMonths(24), placa.VenceEm);
        Assert.False(renovacao.Confirmar(placa, Venda.AddMonths(13)));
        Assert.Equal(Venda.AddMonths(24), placa.VenceEm);
    }

    [Fact]
    public void Placa_sem_venda_nao_renova() =>
        Assert.Throws<RegraDeNegocioException>(() => new Renovacao(new Placa(new PlacaEmitida("K7M2PX", 1, Guid.NewGuid()), 0, Venda), 10_000, 6_000, Venda));
}

public sealed class LancamentoTest
{
    [Fact]
    public void Competencia_e_o_mes_em_Brasilia()
    {
        // 1h da manhã de 1º de novembro em UTC ainda é 31 de outubro em Brasília.
        Assert.Equal("2026-10", Lancamento.CompetenciaDe(new DateTimeOffset(2026, 11, 1, 1, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Estorno_e_a_linha_contraria_que_aponta_para_a_original()
    {
        var original = new Lancamento(TipoDeLancamento.ComissaoDevida, Guid.NewGuid(), -2_998, "Comissão", new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero));
        var estorno = original.Estorno("pagamento estornado", new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(2_998, estorno.ValorCents);
        Assert.Equal(original.Id, estorno.EstornaId);
        Assert.Equal("2026-10", estorno.Competencia);
    }
}
