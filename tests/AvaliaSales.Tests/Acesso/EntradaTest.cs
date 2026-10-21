using System.Net;
using System.Net.Http.Json;
using AvaliaSales.Tests.Apoio;
using Microsoft.IdentityModel.JsonWebTokens;

namespace AvaliaSales.Tests.Acesso;

[Collection(ColecaoDaApi.Nome)]
public sealed class EntradaTest(ApiDeTeste api)
{
    [Fact]
    public async Task Entrar_com_a_senha_certa_devolve_token_com_o_revendedor_do_usuario()
    {
        var (revendedorId, email) = await api.CriarRevendedor("Revenda Norte");
        var cliente = api.Fabrica.CreateClient();

        var resposta = await cliente.PostAsJsonAsync("/auth/entrar", new { email, senha = ApiDeTeste.Senha }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var token = await resposta.Content.ReadFromJsonAsync<ApiDeTeste.TokenLido>(TestContext.Current.CancellationToken);
        var lido = new JsonWebTokenHandler().ReadJsonWebToken(token!.Token);
        Assert.Equal(revendedorId.ToString(), lido.GetClaim("revendedor_id").Value);
        Assert.InRange(token.ExpiraEm - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15));
    }

    [Fact]
    public async Task Senha_errada_e_email_inexistente_respondem_igual()
    {
        var (_, email) = await api.CriarRevendedor("Revenda Sul");
        var cliente = api.Fabrica.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        var senhaErrada = await cliente.PostAsJsonAsync("/auth/entrar", new { email, senha = "outra-senha-qualquer" }, ct);
        var semConta = await cliente.PostAsJsonAsync("/auth/entrar", new { email = "ninguem@teste.com.br", senha = ApiDeTeste.Senha }, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, senhaErrada.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, semConta.StatusCode);
    }

    [Fact]
    public async Task Catalogo_sem_token_responde_401()
    {
        var resposta = await api.Fabrica.CreateClient().GetAsync("/produtos", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }
}
