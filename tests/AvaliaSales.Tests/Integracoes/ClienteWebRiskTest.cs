using System.Net;
using AvaliaSales.Infrastructure.Integracoes.Google;
using AvaliaSales.Tests.Apoio;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Tests.Integracoes;

public sealed class ClienteWebRiskTest
{
    private const string Perigoso = "http://testsafebrowsing.appspot.com/s/malware.html";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ClienteWebRisk Criar(HttpMessageHandler manipulador, string chave = "chave-de-teste", int tempoLimite = 3) =>
        new(new HttpClient(manipulador), Options.Create(new OpcoesWebRisk { ChaveApi = chave, TempoLimiteEmSegundos = tempoLimite }),
            new MemoryCache(new MemoryCacheOptions()), TimeProvider.System, NullLogger<ClienteWebRisk>.Instance);

    [Fact]
    public async Task Sem_chave_aprova_sem_chamar_a_rede()
    {
        var http = new HttpFalso();

        Assert.False(await Criar(http, chave: "").Perigoso(Perigoso, Ct));
        Assert.Empty(http.Pedidos);
    }

    [Fact]
    public async Task Endereco_numa_lista_e_perigoso_e_a_chave_vai_no_cabecalho()
    {
        var http = new HttpFalso((HttpStatusCode.OK, """{"threat":{"threatTypes":["MALWARE"],"expireTime":"2026-10-27T15:01:23Z"}}"""));

        Assert.True(await Criar(http).Perigoso(Perigoso, Ct));
        var pedido = Assert.Single(http.Pedidos);
        Assert.Equal(HttpMethod.Get, pedido.Metodo);
        Assert.Equal("webrisk.googleapis.com", pedido.Uri.Host);
        Assert.Equal("/v1/uris:search", pedido.Uri.AbsolutePath);
        Assert.Contains("threatTypes=MALWARE&threatTypes=SOCIAL_ENGINEERING&threatTypes=UNWANTED_SOFTWARE", pedido.Uri.Query, StringComparison.Ordinal);
        Assert.Contains($"uri={Uri.EscapeDataString(Perigoso)}", pedido.Uri.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("chave-de-teste", pedido.Uri.ToString(), StringComparison.Ordinal);
        Assert.Equal("chave-de-teste", http.Cabecalhos[0]["X-Goog-Api-Key"]);
    }

    [Fact]
    public async Task Resposta_vazia_e_endereco_fora_das_listas()
    {
        Assert.False(await Criar(new HttpFalso((HttpStatusCode.OK, "{}"))).Perigoso("https://loja.exemplo.com.br/", Ct));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, """{"error":{"code":403}}""")]
    [InlineData(HttpStatusCode.TooManyRequests, "{}")]
    [InlineData(HttpStatusCode.OK, "não é json")]
    [InlineData(HttpStatusCode.OK, "[]")]
    [InlineData(HttpStatusCode.OK, """{"threat":null}""")]
    [InlineData(HttpStatusCode.OK, """{"threat":[]}""")]
    [InlineData(HttpStatusCode.OK, """{"threat":{"threatTypes":"MALWARE"}}""")]
    public async Task Erro_ou_resposta_estranha_aprova(HttpStatusCode status, string corpo)
    {
        Assert.False(await Criar(new HttpFalso((status, corpo))).Perigoso(Perigoso, Ct));
    }

    [Fact]
    public async Task Google_que_nao_responde_no_tempo_aprova_sem_esperar_mais()
    {
        var inicio = TimeProvider.System.GetTimestamp();

        Assert.False(await Criar(new Lento(), tempoLimite: 1).Perigoso(Perigoso, Ct));
        Assert.True(TimeProvider.System.GetElapsedTime(inicio) < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Erro_de_rede_aprova()
    {
        Assert.False(await Criar(new SemRede()).Perigoso(Perigoso, Ct));
    }

    /// <summary>O mesmo pedido repetido, em laço ou em várias placas, não volta ao Google.</summary>
    [Fact]
    public async Task Resposta_fica_guardada_por_endereco()
    {
        var http = new HttpFalso((HttpStatusCode.OK, """{"threat":{"threatTypes":["SOCIAL_ENGINEERING"],"expireTime":"2099-01-01T00:00:00Z"}}"""), (HttpStatusCode.OK, "{}"));
        var cliente = Criar(http);

        Assert.True(await cliente.Perigoso(Perigoso, Ct));
        Assert.True(await cliente.Perigoso(Perigoso, Ct));
        Assert.False(await cliente.Perigoso("https://loja.exemplo.com.br/", Ct));
        Assert.False(await cliente.Perigoso("https://loja.exemplo.com.br/", Ct));
        Assert.Equal(2, http.Pedidos.Count);
    }

    private sealed class Lento : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken ct)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class SemRede : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken ct) =>
            throw new HttpRequestException("Sem rede.");
    }
}
