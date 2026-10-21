using System.Net;
using System.Net.Http.Json;
using AvaliaSales.Domain;
using AvaliaSales.Domain.Catalogo;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Catalogo;

public sealed class ProdutoTest
{
    [Theory]
    [InlineData(-1)]
    [InlineData(long.MinValue)]
    public void Preco_negativo_e_recusado(long precoCents) =>
        Assert.Throws<RegraDeNegocioException>(() => new Produto("Placa", precoCents));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Nome_vazio_e_recusado(string nome) =>
        Assert.Throws<RegraDeNegocioException>(() => new Produto(nome, 100));

    [Fact]
    public void Nome_chega_sem_espacos_nas_pontas() =>
        Assert.Equal("Placa NFC", new Produto("  Placa NFC ", 0).Nome);
}

[Collection(ColecaoDaApi.Nome)]
public sealed class CadastroDeProdutoTest(ApiDeTeste api)
{
    [Fact]
    public async Task Regra_de_negocio_recusada_responde_422_com_a_mensagem()
    {
        var (_, email) = await api.CriarRevendedor("Revenda Regra");
        var cliente = await api.ClienteLogado(email);

        var resposta = await cliente.PostAsJsonAsync("/produtos", new { nome = "Placa", precoCents = -5 }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        Assert.Contains("negativo", await resposta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Health_responde_com_o_banco_no_ar()
    {
        var resposta = await api.Fabrica.CreateClient().GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }
}
