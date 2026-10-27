using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Acesso;

[Collection(ColecaoDaApi.Nome)]
public sealed partial class SegundoFatorTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Com_o_segundo_fator_ligado_a_senha_so_da_um_desafio_e_o_token_sai_com_o_codigo()
    {
        var (_, email) = await api.CriarRevendedor("Revenda 2FA");
        var dono = await api.ClienteLogado(email);
        var (chave, _) = await Ligar(dono);

        var desafio = await Desafio(email);
        var anonimo = api.Fabrica.CreateClient();
        await anonimo.Postar("/auth/entrar/codigo", new { desafio, codigo = "000000" }, HttpStatusCode.UnprocessableEntity);
        var token = await anonimo.Postar("/auth/entrar/codigo", new { desafio, codigo = Totp.Codigo(chave) });

        Assert.False(string.IsNullOrEmpty(token.GetProperty("token").GetString()));
        await anonimo.Postar("/auth/entrar/codigo", new { desafio, codigo = Totp.Codigo(chave) }, HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Codigo_de_recuperacao_entra_uma_vez_e_desligar_pede_codigo()
    {
        var (_, email) = await api.CriarRevendedor("Revenda 2FA Recuperação");
        var dono = await api.ClienteLogado(email);
        var (chave, codigos) = await Ligar(dono);
        await dono.Postar("/conta/segundo-fator/chave", new { }, HttpStatusCode.UnprocessableEntity);

        var anonimo = api.Fabrica.CreateClient();
        await anonimo.Postar("/auth/entrar/codigo", new { desafio = await Desafio(email), codigo = codigos[0] });
        await anonimo.Postar("/auth/entrar/codigo", new { desafio = await Desafio(email), codigo = codigos[0] }, HttpStatusCode.UnprocessableEntity);

        await dono.Postar("/conta/segundo-fator/desligar", new { codigo = "123456" }, HttpStatusCode.UnprocessableEntity);
        await dono.Postar("/conta/segundo-fator/desligar", new { codigo = Totp.Codigo(chave) }, HttpStatusCode.NoContent);
        var entrada = await api.Fabrica.CreateClient().PostAsJsonAsync("/auth/entrar", new { email, senha = ApiDeTeste.Senha }, Ct);
        Assert.Equal(HttpStatusCode.OK, entrada.StatusCode);
    }

    /// <summary>Ligado pelo app, desligar manda um código novo ao e-mail cadastrado, e ele basta.</summary>
    [Fact]
    public async Task Desativar_manda_codigo_novo_ao_email_e_ele_desliga()
    {
        var (_, email) = await api.CriarRevendedor("Revenda 2FA Desativar Email");
        var dono = await api.ClienteLogado(email);
        await Ligar(dono);

        await dono.Postar("/conta/segundo-fator/desligar/codigo", new { }, HttpStatusCode.NoContent);
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var assunto = (await banco.Emails(email)).First(e => e.Assunto.StartsWith("Código de acesso", StringComparison.Ordinal)).Assunto;
        var codigo = System.Text.RegularExpressions.Regex.Match(assunto, @"\d{6}").Value;

        await dono.Postar("/conta/segundo-fator/desligar", new { codigo }, HttpStatusCode.NoContent);
        Assert.False((await dono.Pegar("/conta/segundo-fator")).GetProperty("ligado").GetBoolean());
        await dono.Postar("/conta/segundo-fator/desligar/codigo", new { }, HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>Ligar e desligar o segundo fator avisa o e-mail do dono, sem código nem link de entrada.</summary>
    [Fact]
    public async Task Ligar_e_desligar_o_segundo_fator_avisam_o_email_do_dono()
    {
        var (_, email) = await api.CriarRevendedor("Revenda 2FA Aviso");
        var dono = await api.ClienteLogado(email);
        var (chave, _) = await Ligar(dono);
        await dono.Postar("/conta/segundo-fator/desligar", new { codigo = Totp.Codigo(chave) }, HttpStatusCode.NoContent);

        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var assuntos = (await banco.Emails(email)).Select(e => e.Assunto).ToList();
        Assert.Equal(1, assuntos.Count(a => a == "Segundo fator ligado"));
        Assert.Equal(1, assuntos.Count(a => a == "Segundo fator desligado"));
    }

    [Fact]
    public async Task Link_de_troca_de_senha_nao_pula_o_segundo_fator()
    {
        var (_, email) = await api.CriarRevendedor("Revenda 2FA Senha");
        await Ligar(await api.ClienteLogado(email));
        var anonimo = api.Fabrica.CreateClient();
        await anonimo.Postar("/auth/senha/link", new { email }, HttpStatusCode.NoContent);
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var texto = (await banco.UltimoEmail(email)).Texto;
        var token = LinkDaSenha().Match(texto).Groups[1].Value;

        var resposta = await anonimo.PostAsJsonAsync("/auth/senha", new { token, senha = "senha-nova-com-2fa" }, Ct);

        Assert.Equal(HttpStatusCode.Accepted, resposta.StatusCode);
    }

    [Fact]
    public async Task Vendedor_nao_liga_segundo_fator()
    {
        var revenda = await api.RevendaComEstoque("Revenda 2FA Vendedor");
        var email = $"vendedor-{Guid.NewGuid():N}@teste.com.br";
        await revenda.Dono.Postar("/equipe", new { nome = "Lia", email, senha = ApiDeTeste.Senha }, HttpStatusCode.Created);

        await (await api.ClienteLogado(email)).Postar("/conta/segundo-fator/chave", new { }, HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>A chave do app e os códigos de recuperação, que a API mostra uma vez só.</summary>
    private static async Task<(string Chave, List<string> Recuperacao)> Ligar(HttpClient dono)
    {
        var chave = (await dono.Postar("/conta/segundo-fator/chave", new { })).GetProperty("chave").GetString()!;
        await dono.Postar("/conta/segundo-fator", new { codigo = "000000" }, HttpStatusCode.UnprocessableEntity);
        var codigos = (await dono.Postar("/conta/segundo-fator", new { codigo = Totp.Codigo(chave) })).EnumerateArray().Select(c => c.GetString()!).ToList();
        Assert.Equal(10, codigos.Count);
        Assert.True((await dono.Pegar("/conta/segundo-fator")).GetProperty("ligado").GetBoolean());
        return (chave, codigos);
    }

    private async Task<string> Desafio(string email)
    {
        var resposta = await api.Fabrica.CreateClient().PostAsJsonAsync("/auth/entrar", new { email, senha = ApiDeTeste.Senha }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, resposta.StatusCode);
        return (await resposta.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(Ct)).GetProperty("desafio").GetString()!;
    }

    [GeneratedRegex(@"/senha#([A-Za-z0-9_-]{20,})")]
    private static partial Regex LinkDaSenha();
}
