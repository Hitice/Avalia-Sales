using System.Net;
using System.Text;
using System.Text.Json;
using AvaliaSales.Application;
using AvaliaSales.Application.Contratos;
using AvaliaSales.Application.Pagamentos;
using AvaliaSales.Domain.Contratos;
using AvaliaSales.Domain.Pagamentos;
using AvaliaSales.Infrastructure;
using AvaliaSales.Tests.Apoio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AvaliaSales.Tests.Integracoes;

/// <summary>
/// O que os clientes mandam e entendem, contra um servidor falso: rota,
/// autenticação, corpo e leitura da resposta, nos formatos da referência
/// oficial de cada API. Não substitui o teste no sandbox com chave real.
/// </summary>
public sealed class ContratoHttpTest
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

    private static PedidoNoGateway Pedido(MeioDePagamento meio) => new(
        "0199f0c2a3b44c5d8e9f001122334455", "Placa NFC", 14_990, meio,
        new Pagador("Maria Lima", "maria@cliente.com.br", "123.456.789-09", "+55 (11) 98765-4321"),
        [new RegraDeDivisao("rp_revendedor", 14_692, true), new RegraDeDivisao("rp_avaliaDeTeste", 298, false)],
        meio == MeioDePagamento.Cartao ? "token_abc" : null, 3);

    private const string PedidoPixCriado = """
        {"id":"or_123","code":"0199f0c2a3b44c5d8e9f001122334455","status":"pending",
         "charges":[{"id":"ch_1","status":"pending","last_transaction":{"qr_code":"00020101pix","qr_code_url":"https://api.pagar.me/qr.png","expires_at":"2026-10-21T21:00:00Z"}}]}
        """;

    [Fact]
    public async Task Pedido_pix_vai_com_basic_auth_codigo_e_split_em_centavos()
    {
        var http = new HttpFalso((HttpStatusCode.OK, PedidoPixCriado));
        await using var servicos = Servicos(http);

        var criado = await servicos.GetRequiredService<IGatewayDePagamento>().CriarPedido(Pedido(MeioDePagamento.Pix), Ct);

        var (metodo, uri, corpo, autorizacao) = Assert.Single(http.Pedidos);
        Assert.Equal(HttpMethod.Post, metodo);
        Assert.Equal("https://api.pagar.me/core/v5/orders", uri.ToString());
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes("sk_test_chave_de_teste:")), autorizacao);

        using var json = JsonDocument.Parse(corpo!);
        var raiz = json.RootElement;
        Assert.Equal("0199f0c2a3b44c5d8e9f001122334455", raiz.GetProperty("code").GetString());
        Assert.Equal(14_990, raiz.GetProperty("items")[0].GetProperty("amount").GetInt32());
        Assert.Equal("12345678909", raiz.GetProperty("customer").GetProperty("document").GetString());
        Assert.Equal("11", raiz.GetProperty("customer").GetProperty("phones").GetProperty("mobile_phone").GetProperty("area_code").GetString());
        var pagamento = raiz.GetProperty("payments")[0];
        Assert.Equal("pix", pagamento.GetProperty("payment_method").GetString());
        Assert.Equal(1800, pagamento.GetProperty("pix").GetProperty("expires_in").GetInt32());
        var split = pagamento.GetProperty("split");
        Assert.Equal(14_990, split.EnumerateArray().Sum(r => r.GetProperty("amount").GetInt32()));
        Assert.All(split.EnumerateArray(), r => Assert.Equal("flat", r.GetProperty("type").GetString()));
        Assert.True(split[0].GetProperty("options").GetProperty("liable").GetBoolean());
        Assert.False(split[1].GetProperty("options").GetProperty("charge_processing_fee").GetBoolean());

        Assert.Equal("or_123", criado.Id);
        Assert.Equal("00020101pix", criado.PixCopiaECola);
        Assert.Equal(SituacaoDaCobranca.Pendente, criado.Situacao);
    }

    /// <summary>A criação no Pagar.me leva o código da cobrança no Idempotency-key: o reenvio da mesma criação não gera outro pedido.</summary>
    [Fact]
    public async Task Criar_pedido_e_link_leva_o_codigo_da_cobranca_no_cabecalho_de_idempotencia()
    {
        var http = new HttpFalso((HttpStatusCode.OK, PedidoPixCriado), (HttpStatusCode.OK, """{"id":"pl_abc","url":"https://link.pagar.me/pl_abc"}"""));
        await using var servicos = Servicos(http);
        var gateway = servicos.GetRequiredService<IGatewayDePagamento>();

        await gateway.CriarPedido(Pedido(MeioDePagamento.Pix), Ct);
        await gateway.CriarLink(new LinkNoGateway("0199f0c2a3b44c5d8e9f00112233aaaa", "Pedido de 50 placas", 70_000, 4_320), Ct);

        Assert.Equal(2, http.Cabecalhos.Count);
        Assert.Equal("0199f0c2a3b44c5d8e9f001122334455", http.Cabecalhos[0].GetValueOrDefault("Idempotency-Key"));
        Assert.Equal("https://api.pagar.me/core/v5/paymentlinks", http.Pedidos[1].Uri.ToString());
        Assert.Equal("0199f0c2a3b44c5d8e9f00112233aaaa", http.Cabecalhos[1].GetValueOrDefault("Idempotency-Key"));
    }

    [Fact]
    public async Task Pedido_com_cartao_leva_o_token_e_nunca_o_numero_do_cartao()
    {
        var http = new HttpFalso((HttpStatusCode.OK, PedidoPixCriado));
        await using var servicos = Servicos(http);

        await servicos.GetRequiredService<IGatewayDePagamento>().CriarPedido(Pedido(MeioDePagamento.Cartao), Ct);

        using var json = JsonDocument.Parse(Assert.Single(http.Pedidos).Corpo!);
        var cartao = json.RootElement.GetProperty("payments")[0].GetProperty("credit_card");
        Assert.Equal("token_abc", cartao.GetProperty("card_token").GetString());
        Assert.Equal(3, cartao.GetProperty("installments").GetInt32());
        Assert.False(cartao.TryGetProperty("card", out _));
    }

    /// <summary>Repetir um POST de cobrança pode cobrar duas vezes; o handler de resiliência não pode repetir.</summary>
    [Fact]
    public async Task Erro_do_pagarme_ao_criar_pedido_nao_e_repetido()
    {
        var http = new HttpFalso((HttpStatusCode.ServiceUnavailable, "{\"message\":\"indisponivel\"}"), (HttpStatusCode.OK, PedidoPixCriado));
        await using var servicos = Servicos(http);

        var erro = await Assert.ThrowsAsync<IntegracaoException>(() =>
            servicos.GetRequiredService<IGatewayDePagamento>().CriarPedido(Pedido(MeioDePagamento.Pix), Ct));

        Assert.Equal(503, erro.Status);
        Assert.Single(http.Pedidos);
        Assert.DoesNotContain("sk_test", erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Busca_por_codigo_le_a_lista_e_reconhece_pago_e_estornado()
    {
        var http = new HttpFalso(
            (HttpStatusCode.OK, """{"data":[{"id":"or_1","code":"abc","status":"paid","charges":[{"status":"paid","paid_at":"2026-10-21T20:00:00Z"}]}],"paging":{}}"""),
            (HttpStatusCode.OK, """{"data":[{"id":"or_1","code":"abc","status":"paid","charges":[{"status":"refunded"}]}],"paging":{}}"""),
            (HttpStatusCode.OK, """{"data":[],"paging":{}}"""));
        await using var servicos = Servicos(http);
        var gateway = servicos.GetRequiredService<IGatewayDePagamento>();

        var pago = await gateway.BuscarPorCodigo("abc", Ct);
        var estornado = await gateway.BuscarPorCodigo("abc", Ct);
        var inexistente = await gateway.BuscarPorCodigo("abc", Ct);

        Assert.Equal("https://api.pagar.me/core/v5/orders?code=abc", http.Pedidos[0].Uri.ToString());
        Assert.Equal(SituacaoDaCobranca.Paga, pago!.Situacao);
        Assert.Equal(new DateTimeOffset(2026, 10, 21, 20, 0, 0, TimeSpan.Zero), pago.PagoEm);
        Assert.Equal(SituacaoDaCobranca.Estornada, estornado!.Situacao);
        Assert.Null(inexistente);
    }

    [Fact]
    public async Task Cancelar_pedido_le_as_cobrancas_e_cancela_so_a_pendente_ou_paga()
    {
        var http = new HttpFalso(
            (HttpStatusCode.OK, """{"id":"or_1","charges":[{"id":"ch_paga","status":"paid"},{"id":"ch_falhou","status":"failed"}]}"""),
            (HttpStatusCode.OK, """{"id":"ch_paga","status":"refunded"}"""));
        await using var servicos = Servicos(http);

        await servicos.GetRequiredService<IGatewayDePagamento>().CancelarPedido("or_1", Ct);

        Assert.Equal(2, http.Pedidos.Count);
        Assert.Equal("https://api.pagar.me/core/v5/orders/or_1", http.Pedidos[0].Uri.ToString());
        Assert.Equal((HttpMethod.Delete, "https://api.pagar.me/core/v5/charges/ch_paga"), (http.Pedidos[1].Metodo, http.Pedidos[1].Uri.ToString()));
    }

    [Fact]
    public async Task Cancelar_link_e_um_patch_sem_corpo_no_cancel_do_link()
    {
        var http = new HttpFalso((HttpStatusCode.NoContent, ""));
        await using var servicos = Servicos(http);

        await servicos.GetRequiredService<IGatewayDePagamento>().CancelarLink("pl_abc", Ct);

        Assert.Equal((HttpMethod.Patch, "https://api.pagar.me/core/v5/paymentlinks/pl_abc/cancel"), (http.Pedidos.Single().Metodo, http.Pedidos.Single().Uri.ToString()));
    }

    [Fact]
    public async Task Recebedor_e_lido_pelo_id()
    {
        var http = new HttpFalso((HttpStatusCode.OK, """{"id":"rp_abc","name":"Avalia","status":"active"}"""));
        await using var servicos = Servicos(http);

        var recebedor = await servicos.GetRequiredService<IGatewayDePagamento>().BuscarRecebedor("rp_abc", Ct);

        Assert.Equal("https://api.pagar.me/core/v5/recipients/rp_abc", http.Pedidos[0].Uri.ToString());
        Assert.Equal(new RecebedorDoGateway("rp_abc", "Avalia", "active"), recebedor);
    }

    [Fact]
    public async Task Envio_a_d4sign_segue_as_rotas_com_token_e_cryptkey_na_url()
    {
        var http = new HttpFalso(
            (HttpStatusCode.OK, """{"uuid":"doc-123"}"""),
            (HttpStatusCode.OK, "{}"),
            (HttpStatusCode.OK, "{}"),
            (HttpStatusCode.OK, "{}"),
            (HttpStatusCode.OK, """{"uuidDoc":"doc-123","statusId":"4","statusName":"Finalizado"}"""),
            (HttpStatusCode.OK, """[{"uuidDoc":"doc-123","statusId":"3"}]"""));
        await using var servicos = Servicos(http);
        var d4sign = servicos.GetRequiredService<IAssinaturaEletronica>();

        var documento = await d4sign.EnviarAoCofre("Contrato", Pdf.Minimo(), Ct);
        await d4sign.CadastrarWebhook(documento, Ct);
        await d4sign.CadastrarSignatario(documento, "ana@revenda.com.br", Ct);
        await d4sign.EnviarParaAssinatura(documento, "Assine, por favor.", Ct);
        var finalizado = await d4sign.LerSituacao(documento, Ct);
        var emAndamento = await d4sign.LerSituacao(documento, Ct);

        Assert.Equal("doc-123", documento);
        var caminhos = http.Pedidos.Select(p => p.Uri.AbsolutePath).ToList();
        Assert.Equal(
            ["/api/v1/documents/cofre-de-teste/uploadbinary", "/api/v1/documents/doc-123/webhooks", "/api/v1/documents/doc-123/createlist",
             "/api/v1/documents/doc-123/sendtosigner", "/api/v1/documents/doc-123", "/api/v1/documents/doc-123"],
            caminhos);
        Assert.All(http.Pedidos, p => Assert.Equal("?tokenAPI=token-de-teste&cryptKey=crypt-de-teste", p.Uri.Query));
        Assert.Contains("\"base64_binary_file\"", http.Pedidos[0].Corpo, StringComparison.Ordinal);
        Assert.Contains("\"url\":\"https://api.exemplo.com.br/webhooks/d4sign\"", http.Pedidos[1].Corpo, StringComparison.Ordinal);
        Assert.Contains("\"act\":\"1\"", http.Pedidos[2].Corpo, StringComparison.Ordinal);
        Assert.Equal(SituacaoDoContrato.Assinado, finalizado);
        Assert.Null(emAndamento);
    }

    [Fact]
    public async Task Erro_da_d4sign_nao_expoe_o_token_da_url()
    {
        var http = new HttpFalso((HttpStatusCode.Unauthorized, "{\"message\":\"tokenAPI invalido\"}"));
        await using var servicos = Servicos(http);

        var erro = await Assert.ThrowsAsync<IntegracaoException>(() =>
            servicos.GetRequiredService<IAssinaturaEletronica>().CadastrarWebhook("doc-1", Ct));

        Assert.Equal(401, erro.Status);
        Assert.DoesNotContain("token-de-teste", erro.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("crypt-de-teste", erro.Message, StringComparison.Ordinal);
    }
}
