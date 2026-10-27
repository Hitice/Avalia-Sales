using System.Net;
using System.Text.Json;
using AvaliaSales.Application;
using AvaliaSales.Application.Erp;
using AvaliaSales.Infrastructure.Integracoes;
using AvaliaSales.Infrastructure.Integracoes.Protheus;
using AvaliaSales.Tests.Apoio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Tests.Integracoes;

/// <summary>ERP de mentira: guarda o que recebeu, falha quando o teste mandar e devolve o mesmo número para a mesma chave.</summary>
public sealed class ErpFalso : IErp
{
    public List<PedidoDeVendaNoErp> Recebidos { get; } = [];
    public bool Falhar { get; set; }
    public bool Configurado => true;

    public Task<string> EnviarPedidoDeVenda(PedidoDeVendaNoErp pedido, CancellationToken ct)
    {
        Recebidos.Add(pedido);
        return Falhar ? throw new IntegracaoException("Protheus", 503, "fora do ar") : Task.FromResult("PV" + pedido.Chave[..6]);
    }
}

public sealed class ContratoDoProtheusTest
{
    [Fact]
    public async Task Pedido_de_venda_vai_com_autenticacao_basica_e_volta_com_o_numero()
    {
        var http = new HttpFalso((HttpStatusCode.Created, """{"numero":"PV000123"}"""));
        var cliente = new ClienteProtheus(new HttpClient(http) { BaseAddress = new Uri("https://erp.exemplo.com.br/rest/") },
            Options.Create(new OpcoesProtheus { UrlBase = "https://erp.exemplo.com.br/rest/", Usuario = "integra", Senha = "s3nha" }));

        var numero = await cliente.EnviarPedidoDeVenda(new PedidoDeVendaNoErp("chave-1", "Revenda", "11222333000181", "PLACA", 20, 1_400, 28_000), TestContext.Current.CancellationToken);

        Assert.Equal("PV000123", numero);
        var (metodo, uri, corpo, autorizacao) = Assert.Single(http.Pedidos);
        Assert.Equal(HttpMethod.Post, metodo);
        Assert.Equal("https://erp.exemplo.com.br/rest/pedidos-de-venda", uri.ToString());
        Assert.Equal("Basic " + Convert.ToBase64String("integra:s3nha"u8.ToArray()), autorizacao);
        using var json = JsonDocument.Parse(corpo!);
        Assert.Equal("chave-1", json.RootElement.GetProperty("chave").GetString());
        Assert.Equal(28_000, json.RootElement.GetProperty("valorTotalCents").GetInt64());
    }

    [Fact]
    public async Task Erro_do_Protheus_vira_IntegracaoException_sem_a_senha()
    {
        var http = new HttpFalso((HttpStatusCode.InternalServerError, """{"erro":"falha"}"""));
        var cliente = new ClienteProtheus(new HttpClient(http) { BaseAddress = new Uri("https://erp.exemplo.com.br/rest/") },
            Options.Create(new OpcoesProtheus { UrlBase = "https://erp.exemplo.com.br/rest/", Usuario = "integra", Senha = "s3nha" }));

        var erro = await Assert.ThrowsAsync<IntegracaoException>(() =>
            cliente.EnviarPedidoDeVenda(new PedidoDeVendaNoErp("c", "R", null, "PLACA", 1, 1, 1), TestContext.Current.CancellationToken));
        Assert.DoesNotContain("s3nha", erro.Message, StringComparison.Ordinal);
    }
}

[Collection(ColecaoDaApi.Nome)]
public sealed class FluxoDoErpTest(ApiDeTeste api)
{
    [Fact]
    public async Task Pedido_pago_vai_ao_ERP_uma_vez_e_a_falha_volta_para_a_fila()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var (_, email) = await api.CriarRevendedor("Revenda do ERP");
        var dono = await api.ClienteLogado(email);
        var pacote = await Cenarios.PacoteDaQuantidade(operadora, Random.Shared.Next(700, 750), 70_000L);
        var pedido = (await dono.Postar("/pedidos", new { pacoteId = pacote }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await operadora.Postar($"/operadora/pedidos/{pedido}/pagamento", new { }, HttpStatusCode.NoContent);
        var erp = new ErpFalso { Falhar = true };
        await using var worker = api.AmbienteDoWorker(api.Gateway, new AssinaturaFalsa(), erp: erp);

        async Task Rodar()
        {
            using var escopo = worker.CreateScope();
            // A fila é do banco inteiro dos testes, do mais antigo ao mais novo: a volta leva todos, senão este pedido fica de fora.
            await escopo.ServiceProvider.GetRequiredService<EnvioAoErpRotina>().Executar(100_000, TestContext.Current.CancellationToken);
        }

        await Rodar();
        var fila = await operadora.PegarItens("/operadora/erp");
        var envio = fila.EnumerateArray().Single(e => e.GetProperty("pedidoId").GetGuid() == pedido);
        Assert.Equal("Pendente", envio.GetProperty("situacao").GetString());
        Assert.Equal(1, envio.GetProperty("tentativas").GetInt32());

        erp.Falhar = false;
        await Rodar();
        await Rodar();

        var depois = (await operadora.PegarItens("/operadora/erp")).EnumerateArray().Single(e => e.GetProperty("pedidoId").GetGuid() == pedido);
        Assert.Equal("Enviado", depois.GetProperty("situacao").GetString());
        Assert.Equal(2, erp.Recebidos.Count(r => r.Chave == pedido.ToString()));
    }
}
