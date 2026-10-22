using System.Net;
using System.Net.Http.Json;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Acesso;

/// <summary>Refresh em cookie HttpOnly, rotativo: recarregar a página não derruba a sessão, e token copiado não vale duas vezes.</summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class SessaoTest(ApiDeTeste api)
{
    private static string Cookie(HttpResponseMessage resposta) =>
        resposta.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("avalia_sessao=", StringComparison.Ordinal));

    private static HttpRequestMessage Renovar(string cookie)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Post, "/auth/renovar");
        pedido.Headers.Add("Cookie", cookie.Split(';')[0]);
        return pedido;
    }

    [Fact]
    public async Task Cookie_e_HttpOnly_e_renova_o_acesso_trocando_o_token()
    {
        var (_, email) = await api.CriarRevendedor("Revenda da Sessão");
        var cliente = api.Fabrica.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var ct = TestContext.Current.CancellationToken;

        var entrada = await cliente.PostAsJsonAsync("/auth/entrar", new { email, senha = ApiDeTeste.Senha }, ct);
        var primeiro = Cookie(entrada);
        Assert.Contains("httponly", primeiro, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", primeiro, StringComparison.OrdinalIgnoreCase);

        var renovada = await cliente.SendAsync(Renovar(primeiro), ct);
        Assert.Equal(HttpStatusCode.OK, renovada.StatusCode);
        var token = await renovada.Content.ReadFromJsonAsync<ApiDeTeste.TokenLido>(ct);
        Assert.Equal("Dono", token!.Perfil.Papel);
        Assert.NotEqual(primeiro.Split(';')[0], Cookie(renovada).Split(';')[0]);
    }

    [Fact]
    public async Task Token_ja_trocado_apresentado_de_novo_encerra_o_login_inteiro()
    {
        var (_, email) = await api.CriarRevendedor("Revenda do Token Copiado");
        var cliente = api.Fabrica.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var ct = TestContext.Current.CancellationToken;
        var primeiro = Cookie(await cliente.PostAsJsonAsync("/auth/entrar", new { email, senha = ApiDeTeste.Senha }, ct));
        var segundo = Cookie(await cliente.SendAsync(Renovar(primeiro), ct));

        var reuso = await cliente.SendAsync(Renovar(primeiro), ct);
        var legitimo = await cliente.SendAsync(Renovar(segundo), ct);

        Assert.Equal(HttpStatusCode.Unauthorized, reuso.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, legitimo.StatusCode);
    }

    [Fact]
    public async Task Sair_encerra_a_sessao()
    {
        var (_, email) = await api.CriarRevendedor("Revenda da Saída");
        var cliente = api.Fabrica.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var ct = TestContext.Current.CancellationToken;
        var cookie = Cookie(await cliente.PostAsJsonAsync("/auth/entrar", new { email, senha = ApiDeTeste.Senha }, ct));

        var sair = new HttpRequestMessage(HttpMethod.Post, "/auth/sair");
        sair.Headers.Add("Cookie", cookie.Split(';')[0]);
        await cliente.SendAsync(sair, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.SendAsync(Renovar(cookie), ct)).StatusCode);
    }

    [Fact]
    public async Task Cadastro_pela_tela_cria_a_revenda_e_ja_entra_como_dono()
    {
        var cliente = api.Fabrica.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var ct = TestContext.Current.CancellationToken;
        var email = $"novo-{Guid.NewGuid():N}@teste.com.br";

        var resposta = await cliente.PostAsJsonAsync("/cadastro",
            new { empresa = "Revenda Nova", cnpj = "13.186.091/0001-43", celular = "(11) 98765-4321", nome = "Maria Dona", email, senha = ApiDeTeste.Senha }, ct);
        var repetido = await cliente.PostAsJsonAsync("/cadastro",
            new { empresa = "Outra", cnpj = "13186091000143", celular = "(11) 98765-4321", nome = "Outra", email = "x" + email, senha = ApiDeTeste.Senha }, ct);
        var semCelular = await cliente.PostAsJsonAsync("/cadastro",
            new { empresa = "Sem Celular", cnpj = "45.723.174/0001-10", nome = "Outra", email = "y" + email, senha = ApiDeTeste.Senha }, ct);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var token = await resposta.Content.ReadFromJsonAsync<ApiDeTeste.TokenLido>(ct);
        Assert.Equal("Revenda Nova", token!.Perfil.Revendedor);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, repetido.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, semCelular.StatusCode);
    }

    [Fact]
    public async Task Dono_troca_o_celular_da_revenda()
    {
        var (_, email) = await api.CriarRevendedor("Revenda Celular");
        var cliente = await api.ClienteLogado(email);

        await cliente.Colocar("/revenda/celular", new { celular = "(21) 99876-5432" }, HttpStatusCode.NoContent);
        await cliente.Colocar("/revenda/celular", new { celular = "123" }, HttpStatusCode.UnprocessableEntity);

        var revenda = await cliente.Pegar("/revenda");
        Assert.Equal("21998765432", revenda.GetProperty("revendedor").GetProperty("celular").GetString());
    }
}
