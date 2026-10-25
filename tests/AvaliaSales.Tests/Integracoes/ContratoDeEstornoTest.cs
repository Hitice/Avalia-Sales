using System.Net;
using AvaliaSales.Application.Pagamentos;
using AvaliaSales.Domain.Pagamentos;
using AvaliaSales.Infrastructure;
using AvaliaSales.Tests.Apoio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AvaliaSales.Tests.Integracoes;

/// <summary>Estorno como o cliente do Pagar.me o lê e o repete, contra um servidor falso. Não substitui o sandbox.</summary>
public sealed class ContratoDeEstornoTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ServiceProvider Servicos(HttpFalso http)
    {
        var servicos = new ServiceCollection();
        servicos.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(ApiDeTeste.Integracoes).Build());
        servicos.AddLogging();
        servicos.AdicionarIntegracoes(() => http);
        return servicos.BuildServiceProvider();
    }

    [Fact]
    public async Task Cobranca_cancelada_depois_de_paga_e_lida_como_estornada_e_a_cancelada_sem_pagar_segue_cancelada()
    {
        var http = new HttpFalso(
            (HttpStatusCode.OK, """{"data":[{"id":"or_1","code":"abc","status":"canceled","charges":[{"status":"canceled","paid_at":"2026-10-21T20:00:00Z"}]}],"paging":{}}"""),
            (HttpStatusCode.OK, """{"data":[{"id":"or_2","code":"def","status":"canceled","charges":[{"status":"canceled"}]}],"paging":{}}"""));
        await using var servicos = Servicos(http);
        var gateway = servicos.GetRequiredService<IGatewayDePagamento>();

        Assert.Equal(SituacaoDaCobranca.Estornada, (await gateway.BuscarPorCodigo("abc", Ct))!.Situacao);
        Assert.Equal(SituacaoDaCobranca.Cancelada, (await gateway.BuscarPorCodigo("def", Ct))!.Situacao);
    }

    [Fact]
    public async Task Estorno_parcial_repetido_depois_de_resposta_perdida_nao_devolve_de_novo()
    {
        const string Pedido = """{"id":"or_1","status":"paid","charges":[{"id":"ch_1","status":"paid","amount":29980,"canceled_amount":14990}]}""";
        var http = new HttpFalso((HttpStatusCode.OK, Pedido), (HttpStatusCode.OK, Pedido), (HttpStatusCode.OK, "{}"));
        await using var servicos = Servicos(http);
        var gateway = servicos.GetRequiredService<IGatewayDePagamento>();

        // O Pagar.me já devolveu 14990 e o sistema não registrou: repetir não manda o DELETE.
        await gateway.EstornarParte("or_1", 14_990, 0, Ct);
        Assert.Single(http.Pedidos);

        // Já devolvido 14990 e registrado: mais 14990 é estorno novo e sai.
        await gateway.EstornarParte("or_1", 14_990, 14_990, Ct);
        Assert.Equal(3, http.Pedidos.Count);
        Assert.Equal(HttpMethod.Delete, http.Pedidos[2].Metodo);
        Assert.Contains("14990", http.Pedidos[2].Corpo, StringComparison.Ordinal);
    }
}
