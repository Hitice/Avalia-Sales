using System.Net;
using AvaliaSales.Application.Seguranca;
using AvaliaSales.Infrastructure.Integracoes.Turnstile;
using AvaliaSales.Tests.Apoio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Tests.Integracoes;

public sealed class VerificadorTurnstileTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static VerificadorTurnstile Criar(HttpMessageHandler manipulador, string chave = "segredo-de-teste", int tempoLimite = 5) =>
        new(new HttpClient(manipulador), Options.Create(new OpcoesTurnstile { ChaveSecreta = chave, TempoLimiteEmSegundos = tempoLimite }), NullLogger<VerificadorTurnstile>.Instance);

    [Fact]
    public async Task Sem_chave_o_captcha_fica_desligado_e_aprova_sem_chamar_a_rede()
    {
        var http = new HttpFalso();
        var verificador = Criar(http, chave: "");

        Assert.False(verificador.Ligado);
        Assert.Equal(ResultadoDoCaptcha.Aprovado, await verificador.Verificar(null, "1.2.3.4", Ct));
        Assert.Empty(http.Pedidos);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Ligado_sem_token_reprova_sem_chamar_a_rede(string? token)
    {
        var http = new HttpFalso();

        Assert.Equal(ResultadoDoCaptcha.Reprovado, await Criar(http).Verificar(token, null, Ct));
        Assert.Empty(http.Pedidos);
    }

    [Fact]
    public async Task Token_gigante_reprova_sem_chamar_a_rede()
    {
        var http = new HttpFalso();

        Assert.Equal(ResultadoDoCaptcha.Reprovado, await Criar(http).Verificar(new string('a', 2049), null, Ct));
        Assert.Empty(http.Pedidos);
    }

    [Fact]
    public async Task Sucesso_aprova_e_manda_chave_token_e_ip_em_formulario()
    {
        var http = new HttpFalso((HttpStatusCode.OK, """{"success":true}"""));

        var resultado = await Criar(http).Verificar("token-do-widget", "203.0.113.9", Ct);

        Assert.Equal(ResultadoDoCaptcha.Aprovado, resultado);
        var pedido = Assert.Single(http.Pedidos);
        Assert.Equal(HttpMethod.Post, pedido.Metodo);
        Assert.Equal("https://challenges.cloudflare.com/turnstile/v0/siteverify", pedido.Uri.ToString());
        Assert.Equal("secret=segredo-de-teste&response=token-do-widget&remoteip=203.0.113.9", pedido.Corpo);
    }

    [Theory]
    [InlineData("""{"success":false,"error-codes":["invalid-input-response"]}""")]
    [InlineData("""{"success":false,"error-codes":["timeout-or-duplicate"]}""")]
    [InlineData("""{"success":false}""")]
    public async Task Token_recusado_pela_cloudflare_reprova(string corpo)
    {
        Assert.Equal(ResultadoDoCaptcha.Reprovado, await Criar(new HttpFalso((HttpStatusCode.OK, corpo))).Verificar("t", null, Ct));
    }

    [Theory]
    [InlineData("""{"success":false,"error-codes":["invalid-input-secret"]}""")]
    [InlineData("""{"success":false,"error-codes":["internal-error"]}""")]
    [InlineData("não é json")]
    [InlineData("""{"outra":"coisa"}""")]
    [InlineData("[]")]
    public async Task Resposta_que_nao_e_do_visitante_falha_fechada(string corpo)
    {
        Assert.Equal(ResultadoDoCaptcha.Indisponivel, await Criar(new HttpFalso((HttpStatusCode.OK, corpo))).Verificar("t", null, Ct));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Status_de_erro_do_verificador_falha_fechada(HttpStatusCode status)
    {
        Assert.Equal(ResultadoDoCaptcha.Indisponivel, await Criar(new HttpFalso((status, "{}"))).Verificar("t", null, Ct));
    }

    [Fact]
    public async Task Erro_de_rede_falha_fechada()
    {
        Assert.Equal(ResultadoDoCaptcha.Indisponivel, await Criar(new ManipuladorQueFalha(new HttpRequestException("sem rede"))).Verificar("t", null, Ct));
    }

    [Fact]
    public async Task Verificador_que_nao_responde_no_prazo_falha_fechada()
    {
        Assert.Equal(ResultadoDoCaptcha.Indisponivel, await Criar(new ManipuladorQueNaoResponde(), tempoLimite: 1).Verificar("t", null, Ct));
    }

    private sealed class ManipuladorQueFalha(Exception erro) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken ct) => Task.FromException<HttpResponseMessage>(erro);
    }

    private sealed class ManipuladorQueNaoResponde : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("inalcançável");
        }
    }
}
