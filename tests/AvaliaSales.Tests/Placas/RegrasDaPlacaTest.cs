using AvaliaSales.Domain;
using AvaliaSales.Domain.Placas;

namespace AvaliaSales.Tests.Placas;

public sealed class CodigoDaPlacaTest
{
    [Theory]
    [InlineData("k7m2px", "K7M2PX")]
    [InlineData(" K7M-2PX ", "K7M2PX")]
    [InlineData("I0LOAB", "1010AB")]
    [InlineData("k7.m2_px", "K7M2PX")]
    public void Normaliza_o_que_a_pessoa_digita(string digitado, string esperado) =>
        Assert.Equal(esperado, CodigoDaPlaca.Normalizar(digitado));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("K7M2P")]
    [InlineData("K7M2PXA")]
    [InlineData("K7M2PU")]
    [InlineData("K7M2P!")]
    public void Codigo_invalido_vira_vazio_e_nao_um_palpite(string? digitado) =>
        Assert.Equal(string.Empty, CodigoDaPlaca.Normalizar(digitado));

    [Fact]
    public void Sorteio_so_usa_o_alfabeto_Crockford()
    {
        for (var i = 0; i < 2_000; i++)
        {
            var codigo = CodigoDaPlaca.Sortear();
            Assert.Equal(CodigoDaPlaca.Tamanho, codigo.Length);
            Assert.All(codigo, c => Assert.Contains(c, CodigoDaPlaca.Alfabeto));
        }
    }
}

public sealed class DestinoTest
{
    private const string Host = "placas.exemplo.com.br";

    [Fact]
    public void Completa_o_https_que_ninguem_digita() =>
        Assert.Equal("https://padaria.com.br/cardapio", Destino.Conferir("padaria.com.br/cardapio", Host));

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,oi")]
    [InlineData("ftp://arquivos.com.br")]
    [InlineData("https://localhost/x")]
    [InlineData("")]
    public void Recusa_o_que_nao_e_endereco_de_navegador(string destino) =>
        Assert.Throws<RegraDeNegocioException>(() => Destino.Conferir(destino, Host));

    [Fact]
    public void Recusa_placa_que_aponta_para_outra_placa() =>
        Assert.Throws<RegraDeNegocioException>(() => Destino.Conferir("https://PLACAS.exemplo.com.br/Q/K7M2PX", Host));

    [Fact]
    public void Recusa_endereco_maior_que_a_placa_aguenta() =>
        Assert.Throws<RegraDeNegocioException>(() => Destino.Conferir("https://a.com.br/" + new string('x', Destino.TamanhoMaximo), Host));
}

public sealed class LinkDeAvaliacaoTest
{
    [Fact]
    public void Monta_o_formulario_de_avaliacao_pelo_lugar() =>
        Assert.Equal("https://search.google.com/local/writereview?placeid=ChIJ0123456789", LinkDeAvaliacao.De("ChIJ0123456789"));

    [Fact]
    public void Link_colado_com_lugar_vira_o_endereco_canonico() =>
        Assert.Equal("https://search.google.com/local/writereview?placeid=ChIJ0123456789",
            LinkDeAvaliacao.Colado("https://www.google.com/maps?placeid=ChIJ0123456789&hl=pt"));

    [Fact]
    public void Link_g_page_do_perfil_fica_como_veio() =>
        Assert.Equal("https://g.page/r/CbX1abc/review", LinkDeAvaliacao.Colado("g.page/r/CbX1abc/review"));

    [Fact]
    public void Link_fora_do_Google_e_recusado() =>
        Assert.Throws<RegraDeNegocioException>(() => LinkDeAvaliacao.Colado("https://google.com.golpe.net/review"));
}

public sealed class CicloDaPlacaTest
{
    private static readonly DateTimeOffset Venda = new(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);

    private static Placa Nova() => new(new PlacaEmitida("K7M2PX", 1, Guid.NewGuid()), 1_400, Venda.AddDays(-5));

    [Fact]
    public void Placa_sem_venda_nao_vai_ao_ar()
    {
        var placa = Nova();

        Assert.Equal(EstadoDaPlaca.EmEstoque, placa.Estado(Venda));
        Assert.False(placa.Redireciona(Venda));
        Assert.Throws<RegraDeNegocioException>(() => placa.Apontar("https://padaria.com.br"));
    }

    [Fact]
    public void Vendida_vale_um_ano_e_mais_trinta_dias_de_carencia_no_ar()
    {
        var placa = Nova();
        placa.Vender(Guid.NewGuid(), "https://padaria.com.br", Venda);

        Assert.Equal(Venda.AddMonths(12), placa.VenceEm);
        Assert.Equal(EstadoDaPlaca.Ativa, placa.Estado(Venda.AddMonths(12)));
        Assert.Equal(EstadoDaPlaca.Carencia, placa.Estado(Venda.AddMonths(12).AddDays(1)));
        Assert.True(placa.Redireciona(Venda.AddMonths(12).AddDays(30)));
        Assert.Equal(EstadoDaPlaca.Vencida, placa.Estado(Venda.AddMonths(12).AddDays(31)));
        Assert.False(placa.Redireciona(Venda.AddMonths(12).AddDays(31)));
    }

    [Fact]
    public void Placa_vendida_nao_e_vendida_de_novo_nem_consignada()
    {
        var placa = Nova();
        placa.Vender(Guid.NewGuid(), "https://padaria.com.br", Venda);

        Assert.Throws<RegraDeNegocioException>(() => placa.Vender(Guid.NewGuid(), "https://outra.com.br", Venda));
        Assert.Throws<RegraDeNegocioException>(() => placa.Consignar(Guid.NewGuid()));
    }

    [Fact]
    public void Venda_tira_a_placa_da_mao_do_vendedor()
    {
        var placa = Nova();
        placa.Consignar(Guid.NewGuid());
        placa.Vender(Guid.NewGuid(), "https://padaria.com.br", Venda);

        Assert.Null(placa.ConsignadaA);
    }

    [Fact]
    public void Suspender_e_voltar_mantem_o_destino()
    {
        var placa = Nova();
        placa.Vender(Guid.NewGuid(), "https://padaria.com.br", Venda);

        placa.Alternar();
        Assert.False(placa.Redireciona(Venda));
        placa.Alternar();
        Assert.True(placa.Redireciona(Venda));
        Assert.Equal("https://padaria.com.br", placa.Destino);
    }
}

public sealed class LoteTest
{
    [Theory]
    [InlineData("LT-0007", 7)]
    [InlineData("lt 7", 7)]
    [InlineData("0007", 7)]
    [InlineData(" 12 ", 12)]
    public void Le_o_numero_como_o_revendedor_digitar(string digitado, int numero) =>
        Assert.Equal(numero, Lote.LerNumero(digitado));

    [Theory]
    [InlineData("")]
    [InlineData("LT-")]
    [InlineData("0")]
    [InlineData("-3")]
    public void Numero_sem_sentido_e_nulo(string digitado) => Assert.Null(Lote.LerNumero(digitado));

    [Fact]
    public void Lote_de_outro_revendedor_responde_como_nao_encontrado()
    {
        var lote = new Lote(1, 20, DateTimeOffset.UtcNow);
        lote.Despachar(Guid.NewGuid(), Guid.NewGuid(), 20, DateTimeOffset.UtcNow);

        var recusa = Assert.Throws<RegraDeNegocioException>(() => lote.Ativar(Guid.NewGuid(), DateTimeOffset.UtcNow));
        Assert.Contains("não encontrado", recusa.Message);
        Assert.Null(lote.AtivadoEm);
    }

    [Fact]
    public void Despacho_exige_a_mesma_quantidade_do_pedido()
    {
        var lote = new Lote(1, 20, DateTimeOffset.UtcNow);

        Assert.Throws<RegraDeNegocioException>(() => lote.Despachar(Guid.NewGuid(), Guid.NewGuid(), 50, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Rotulo_tem_quatro_digitos() => Assert.Equal("LT-0042", Lote.Rotular(42));
}
