using System.Net;
using System.Text;
using AvaliaSales.Infrastructure.Integracoes.BrasilApi;
using Microsoft.Extensions.Logging.Abstractions;

namespace AvaliaSales.Tests.Integracoes;

public sealed class ClienteBrasilApiTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Resposta real de cnpj/v1 (campos conferidos em 22/10/2026), com o município em maiúsculas sem acento.</summary>
    private const string Empresa = """
        {"cnpj":"19131243000197","razao_social":"OPEN KNOWLEDGE BRASIL","situacao_cadastral":2,"descricao_tipo_de_logradouro":"AVENIDA",
         "logradouro":"PAULISTA 37","numero":"37","complemento":"ANDAR 4","bairro":"BELA VISTA","municipio":"SAO PAULO","uf":"SP","cep":"01311902"}
        """;

    private const string Cep = """{"cep":"01311902","state":"SP","city":"São Paulo","neighborhood":"Bela Vista","street":"Avenida Paulista","service":"open-cep"}""";

    [Fact]
    public async Task Cnpj_traz_a_razao_social_e_o_endereco_com_a_cidade_do_cep()
    {
        var falso = new Respostas(new() { ["/api/cnpj/v1/19131243000197"] = Empresa, ["/api/cep/v2/01311902"] = Cep });
        var cliente = new ClienteBrasilApi(new HttpClient(falso) { BaseAddress = new Uri("https://brasilapi.com.br/api/") }, NullLogger<ClienteBrasilApi>.Instance);

        var empresa = await cliente.PorCnpj("19.131.243/0001-97", Ct);

        Assert.NotNull(empresa);
        Assert.Equal("OPEN KNOWLEDGE BRASIL", empresa.RazaoSocial);
        Assert.True(empresa.Ativa);
        Assert.Equal("Avenida Paulista", empresa.Endereco.Logradouro);
        Assert.Equal("37", empresa.Endereco.Numero);
        Assert.Equal("Andar 4", empresa.Endereco.Complemento);
        Assert.Equal("São Paulo", empresa.Endereco.Cidade);
        Assert.Equal("SP", empresa.Endereco.Uf);
        Assert.Equal("01311902", empresa.Endereco.Cep);
    }

    [Fact]
    public async Task Cnpj_nao_encontrado_ou_fora_do_ar_devolve_nulo_para_a_tela_pedir_digitado()
    {
        var falso = new Respostas([]);
        var cliente = new ClienteBrasilApi(new HttpClient(falso) { BaseAddress = new Uri("https://brasilapi.com.br/api/") }, NullLogger<ClienteBrasilApi>.Instance);

        Assert.Null(await cliente.PorCnpj("19131243000197", Ct));
        Assert.Null(await cliente.PorCep("01311-902", Ct));
        Assert.Null(await cliente.PorCnpj("123", Ct));
    }

    private sealed class Respostas(Dictionary<string, string> porCaminho) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(porCaminho.TryGetValue(request.RequestUri!.AbsolutePath, out var corpo)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(corpo, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("""{"message":"não encontrado"}""", Encoding.UTF8, "application/json") });
    }
}
