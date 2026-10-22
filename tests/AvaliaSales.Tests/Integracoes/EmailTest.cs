using System.Net;
using System.Text.Json;
using AvaliaSales.Application.Avisos;
using AvaliaSales.Domain.Avisos;
using AvaliaSales.Infrastructure;
using AvaliaSales.Tests.Apoio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AvaliaSales.Tests.Integracoes;

/// <summary>O que vai ao Resend: remetente, resposta, HTML com a marca e texto escapado.</summary>
public sealed class EmailTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ServiceProvider Servicos(HttpFalso http) =>
        new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(ApiDeTeste.Integracoes).AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Resend:ChaveApi"] = "re_teste",
                ["Resend:Remetente"] = "Avalia Sales <avisos@exemplo.com.br>",
                ["Resend:ResponderPara"] = "suporte@exemplo.com.br",
            }).Build())
            .AddLogging()
            .AdicionarIntegracoes(() => http)
            .BuildServiceProvider();

    [Fact]
    public async Task Email_da_revenda_sai_com_o_nome_dela_resposta_ao_dono_e_botao_na_cor_da_marca()
    {
        var http = new HttpFalso((HttpStatusCode.OK, """{"id":"em_1"}"""));
        await using var servicos = Servicos(http);
        var email = new EmailNaFila("convite:1", "lia@teste.com.br", "Seu acesso", "Olá <b>Lia</b>.\n\nhttps://painel.exemplo.com.br/convite#abc", DateTimeOffset.UtcNow);
        email.AssinarComo("Padaria \"Boa\" <Ltda>\n", "Dono@Padaria.com.br", "#EE46BC");

        var id = await servicos.GetRequiredService<IEnvioDeEmail>().Enviar(
            new MensagemDeEmail(email.Chave, email.Para, email.Assunto, email.Texto, new RemetenteDoEmail(email.NomeDoRemetente!, email.ResponderPara, email.CorDaMarca)), Ct);

        Assert.Equal("em_1", id);
        var corpo = JsonDocument.Parse(http.Pedidos.Single().Corpo!).RootElement;
        Assert.Equal("\"Padaria Boa Ltda via Avalia Sales\" <avisos@exemplo.com.br>", corpo.GetProperty("from").GetString());
        Assert.Equal("dono@padaria.com.br", corpo.GetProperty("reply_to")[0].GetString());
        var html = corpo.GetProperty("html").GetString()!;
        Assert.Contains("background:#ee46bc", html, StringComparison.Ordinal);
        Assert.Contains("href=\"https://painel.exemplo.com.br/convite#abc\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>Lia</b>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Email_da_plataforma_sai_com_o_remetente_e_a_resposta_configurados()
    {
        var http = new HttpFalso((HttpStatusCode.OK, """{"id":"em_2"}"""));
        await using var servicos = Servicos(http);

        await servicos.GetRequiredService<IEnvioDeEmail>().Enviar(new MensagemDeEmail("senha:1", "ana@teste.com.br", "Trocar a senha", "Texto", null), Ct);

        var corpo = JsonDocument.Parse(http.Pedidos.Single().Corpo!).RootElement;
        Assert.Equal("Avalia Sales <avisos@exemplo.com.br>", corpo.GetProperty("from").GetString());
        Assert.Equal("suporte@exemplo.com.br", corpo.GetProperty("reply_to")[0].GetString());
    }
}
