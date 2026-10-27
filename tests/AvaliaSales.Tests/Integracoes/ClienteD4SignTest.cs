using System.Net;
using System.Text;
using AvaliaSales.Infrastructure.Integracoes.D4Sign;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Tests.Integracoes;

public sealed class ClienteD4SignTest
{
    private static ClienteD4Sign Cliente(HttpStatusCode status, string corpo) =>
        new(new HttpClient(new Resposta(status, corpo)) { BaseAddress = new Uri("https://sandbox.d4sign.com.br/api/v1/") },
            Options.Create(new OpcoesD4Sign { TokenApi = "token", CryptKey = "chave", Cofre = "cofre", UrlDoWebhook = "https://x/webhooks/d4sign", SegredoHmac = "segredo-hmac-de-teste-com-folga" }));

    [Fact]
    public async Task Envio_que_ja_esta_em_assinatura_conta_como_feito()
    {
        var cliente = Cliente(HttpStatusCode.BadRequest, """{"message":"This file is already in the signature phase","mensagem_pt":"O documento já foi enviado para assinatura."}""");

        var erro = await Record.ExceptionAsync(() => cliente.EnviarParaAssinatura("uuid-do-documento", "Contrato de revenda", TestContext.Current.CancellationToken));

        Assert.Null(erro);
    }

    [Fact]
    public async Task Outra_recusa_da_D4Sign_continua_sendo_erro()
    {
        var cliente = Cliente(HttpStatusCode.BadRequest, """{"message":"Signer list is empty"}""");

        await Assert.ThrowsAsync<AvaliaSales.Application.IntegracaoException>(() =>
            cliente.EnviarParaAssinatura("uuid-do-documento", "Contrato de revenda", TestContext.Current.CancellationToken));
    }

    private sealed class Resposta(HttpStatusCode status, string corpo) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(corpo, Encoding.UTF8, "application/json") });
    }
}
