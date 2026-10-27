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

        // Fora da janela de tolerância (ToleranciaDoRefreshTest): a troca é recuada no tempo.
        await using (var conexao = new Npgsql.NpgsqlConnection(api.ConexaoDoDono))
        {
            await conexao.OpenAsync(ct);
            await ApiDeTeste.Executar(conexao, $"update sessoes set encerrada_em = now() - interval '1 minute' where usuario_id = (select id from usuarios where email = '{email}') and encerrada_em is not null");
        }

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
    public async Task Cada_um_altera_os_proprios_dados_no_menu_da_conta()
    {
        var (_, email) = await api.CriarRevendedor("Revenda Dados");
        var dono = await api.ClienteLogado(email);

        var dados = await dono.Pegar("/conta/dados");
        Assert.Equal(email, dados.GetProperty("email").GetString());
        Assert.Equal("Revenda Dados", dados.GetProperty("empresa").GetProperty("nome").GetString());

        await dono.Colocar("/conta/dados", new { nome = "Dona Renomeada" });
        var empresa = await dono.Colocar("/conta/dados/empresa", new { nome = "Revenda Dados Nova", celular = "(21) 99876-5432" });
        Assert.Equal("Dona Renomeada", empresa.GetProperty("nome").GetString());
        Assert.Equal("21998765432", empresa.GetProperty("empresa").GetProperty("celular").GetString());
        Assert.Equal("Revenda Dados Nova", empresa.GetProperty("empresa").GetProperty("nome").GetString());
        await dono.Colocar("/conta/dados/empresa", new { nome = "Revenda Dados Nova", celular = "123" }, HttpStatusCode.UnprocessableEntity);
        await dono.Colocar("/conta/dados", new { nome = "" }, HttpStatusCode.UnprocessableEntity);

        var emailDoVendedor = $"vendedor-{Guid.NewGuid():N}@teste.com.br";
        await dono.Postar("/equipe", new { nome = "Vendedor Dados", email = emailDoVendedor, senha = ApiDeTeste.Senha }, HttpStatusCode.Created);
        var vendedor = await api.ClienteLogado(emailDoVendedor);
        var recebimento = await vendedor.Colocar("/conta/dados/recebimento", new { telefone = "(34) 99999-0000", chavePix = "vendedor@pix.com.br" });
        Assert.Equal("vendedor@pix.com.br", recebimento.GetProperty("recebimento").GetProperty("chavePix").GetString());
        await vendedor.Colocar("/conta/dados", new { nome = "Vendedor Renomeado" });
        var equipe = await dono.Pegar("/equipe");
        Assert.Contains(equipe.EnumerateArray(), v => v.GetProperty("nome").GetString() == "Vendedor Renomeado" && v.GetProperty("telefone").GetString() == "34999990000");
        Assert.Equal(HttpStatusCode.Forbidden, (await vendedor.PutAsJsonAsync("/conta/dados/empresa", new { nome = "Intruso", celular = "21998765432" }, TestContext.Current.CancellationToken)).StatusCode);

        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        Assert.Equal(HttpStatusCode.Forbidden, (await operadora.PutAsJsonAsync("/conta/dados/empresa", new { nome = "Intrusa", celular = "21998765432" }, TestContext.Current.CancellationToken)).StatusCode);
    }
}
