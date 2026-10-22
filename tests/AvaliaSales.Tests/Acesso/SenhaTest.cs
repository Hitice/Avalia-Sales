using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Acesso;

[Collection(ColecaoDaApi.Nome)]
public sealed partial class SenhaTest(ApiDeTeste api)
{
    [Fact]
    public async Task Vendedor_sem_senha_cria_a_dele_pelo_convite_e_o_link_vale_uma_vez()
    {
        var (_, emailDoDono) = await api.CriarRevendedor("Revenda Convite");
        var dono = await api.ClienteLogado(emailDoDono);
        var email = $"vendedor-{Guid.NewGuid():N}@teste.com.br";

        var criado = await dono.Postar("/equipe", new { nome = "Lia", email }, HttpStatusCode.Created);

        Assert.True(criado.GetProperty("convitePendente").GetBoolean());
        var token = TokenDoLink(criado.GetProperty("linkDoConvite").GetString()!);
        Assert.Equal(token, TokenDoLink(await UltimoEmail(email)));
        var semSenha = await api.Fabrica.CreateClient().PostAsJsonAsync("/auth/entrar", new { email, senha = "" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, semSenha.StatusCode);

        var anonimo = api.Fabrica.CreateClient();
        var entrou = await anonimo.Postar("/auth/senha", new { token, senha = ApiDeTeste.Senha });
        Assert.Equal("Vendedor", entrou.GetProperty("perfil").GetProperty("papel").GetString());
        await anonimo.Postar("/auth/senha", new { token, senha = "outra-senha-longa" }, HttpStatusCode.UnprocessableEntity);
        await api.ClienteLogado(email);

        var equipe = await dono.Pegar("/equipe");
        Assert.False(equipe.EnumerateArray().Single(v => v.GetProperty("email").GetString() == email).GetProperty("convitePendente").GetBoolean());
        var vendedor = criado.GetProperty("id").GetGuid();
        await dono.Postar($"/equipe/{vendedor}/convite", new { }, HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Convite_reenviado_derruba_o_link_anterior()
    {
        var (_, emailDoDono) = await api.CriarRevendedor("Revenda Reenvio");
        var dono = await api.ClienteLogado(emailDoDono);
        var criado = await dono.Postar("/equipe", new { nome = "Rui", email = $"vendedor-{Guid.NewGuid():N}@teste.com.br" }, HttpStatusCode.Created);
        var primeiro = TokenDoLink(criado.GetProperty("linkDoConvite").GetString()!);

        var reenviado = await dono.Postar($"/equipe/{criado.GetProperty("id").GetGuid()}/convite", new { });

        var anonimo = api.Fabrica.CreateClient();
        await anonimo.Postar("/auth/senha", new { token = primeiro, senha = ApiDeTeste.Senha }, HttpStatusCode.UnprocessableEntity);
        await anonimo.Postar("/auth/senha", new { token = TokenDoLink(reenviado.GetProperty("linkDoConvite").GetString()!), senha = ApiDeTeste.Senha });
    }

    [Fact]
    public async Task Esqueci_a_senha_responde_igual_com_e_sem_conta_e_a_troca_encerra_as_sessoes()
    {
        var (_, email) = await api.CriarRevendedor("Revenda Esqueci");
        var logado = await api.ClienteLogado(email);
        var anonimo = api.Fabrica.CreateClient();
        var semConta = $"ninguem-{Guid.NewGuid():N}@teste.com.br";

        await anonimo.Postar("/auth/senha/link", new { email = semConta }, HttpStatusCode.NoContent);
        await anonimo.Postar("/auth/senha/link", new { email = email.ToUpperInvariant() }, HttpStatusCode.NoContent);

        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(0, await banco.Contar($"select count(*) from emails where para = '{semConta}'"));
        await anonimo.Postar("/auth/senha", new { token = TokenDoLink(await UltimoEmail(email)), senha = "senha-nova-do-dono" });

        var antiga = await anonimo.PostAsJsonAsync("/auth/entrar", new { email, senha = ApiDeTeste.Senha }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, antiga.StatusCode);
        // Sobra aberta só a sessão que o link abriu; a do login anterior caiu.
        Assert.Equal(1, await banco.Contar($"select count(*) from sessoes s join usuarios u on u.id = s.usuario_id where u.email = '{email}' and s.encerrada_em is null"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await logado.PostAsync("/auth/renovar", null, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Senha_curta_pelo_link_e_recusada_e_o_link_continua_valendo()
    {
        var (_, email) = await api.CriarRevendedor("Revenda Curta");
        var anonimo = api.Fabrica.CreateClient();
        await anonimo.Postar("/auth/senha/link", new { email }, HttpStatusCode.NoContent);
        var token = TokenDoLink(await UltimoEmail(email));

        await anonimo.Postar("/auth/senha", new { token, senha = "curta" }, HttpStatusCode.UnprocessableEntity);
        await anonimo.Postar("/auth/senha", new { token, senha = "agora-uma-senha-longa" });
    }

    private async Task<string> UltimoEmail(string para)
    {
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        return await banco.Texto($"select texto from emails where para = '{para}' order by criado_em desc limit 1");
    }

    private static string TokenDoLink(string texto) => Link().Match(texto) is { Success: true } achado
        ? achado.Groups[1].Value
        : throw new Xunit.Sdk.XunitException($"Sem link de senha em: {texto}");

    [GeneratedRegex(@"/(?:convite|senha)#([A-Za-z0-9_-]{20,})")]
    private static partial Regex Link();
}
