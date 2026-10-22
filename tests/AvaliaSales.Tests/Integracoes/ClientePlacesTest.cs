using System.Net;
using System.Text;
using AvaliaSales.Infrastructure.Integracoes.Google;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Tests.Integracoes;

public sealed class ClientePlacesTest
{
    [Fact]
    public async Task Busca_chama_o_text_search_na_base_v1_com_a_chave_e_a_mascara()
    {
        var gravador = new Gravador("""{"places":[{"id":"ChIJabc","displayName":{"text":"Padaria Le Crocant"},"formattedAddress":"Uberlândia - MG"}]}""");
        var http = new HttpClient(gravador) { BaseAddress = new Uri(new OpcoesGoogle().UrlDoPlaces) };
        var cliente = new ClientePlaces(http, Options.Create(new OpcoesGoogle { ChaveDoServidor = "chave-de-teste" }));

        var lugares = await cliente.Buscar("Padaria Uberlândia", TestContext.Current.CancellationToken);

        Assert.Equal("https://places.googleapis.com/v1/places:searchText", gravador.Pedido!.RequestUri!.ToString());
        Assert.Equal(HttpMethod.Post, gravador.Pedido.Method);
        Assert.Equal("chave-de-teste", gravador.Pedido.Headers.GetValues("X-Goog-Api-Key").Single());
        Assert.Equal("places.id,places.displayName,places.formattedAddress", gravador.Pedido.Headers.GetValues("X-Goog-FieldMask").Single());
        var lugar = Assert.Single(lugares);
        Assert.Equal("Padaria Le Crocant", lugar.Nome);
    }

    private sealed class Gravador(string resposta) : HttpMessageHandler
    {
        public HttpRequestMessage? Pedido { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Pedido = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(resposta, Encoding.UTF8, "application/json") });
        }
    }
}
