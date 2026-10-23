using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Acesso;

/// <summary>O cadastro temporário da operadora pela tela: só e-mail da Avalia, só com o código do e-mail.</summary>
[Collection(ColecaoDaApi.Nome)]
public sealed partial class CadastroDeOperadoraTest(ApiDeTeste api)
{
    [Fact]
    public async Task Email_da_avalia_com_o_codigo_cria_a_operadora_que_ja_entra()
    {
        var anonimo = api.Fabrica.CreateClient();
        var email = $"operacao-{Guid.NewGuid():N}@avaliaone.com.br";
        Assert.True((await anonimo.Pegar("/cadastro/operadora")).GetProperty("aberto").GetBoolean());

        await anonimo.Postar("/cadastro/operadora/codigo", new { nome = "Operação", email = "alguem@gmail.com" }, HttpStatusCode.UnprocessableEntity);
        await anonimo.Postar("/cadastro/operadora/codigo", new { nome = "Operação", email }, HttpStatusCode.NoContent);
        var entradaAntes = await anonimo.PostAsJsonAsync("/auth/entrar", new { email, senha = ApiDeTeste.Senha }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, entradaAntes.StatusCode);

        await anonimo.Postar("/cadastro/operadora", new { email, codigo = "000000", senha = ApiDeTeste.Senha }, HttpStatusCode.UnprocessableEntity);
        var codigo = await UltimoCodigo(email);
        await anonimo.Postar("/cadastro/operadora", new { email, codigo, senha = "curta" }, HttpStatusCode.UnprocessableEntity);
        var entrou = await anonimo.Postar("/cadastro/operadora", new { email, codigo, senha = ApiDeTeste.Senha });

        Assert.Equal("Operadora", entrou.GetProperty("perfil").GetProperty("papel").GetString());
        var operadora = await api.ClienteLogado(email);
        Assert.Equal(HttpStatusCode.OK, (await operadora.GetAsync("/operadora/pedidos", TestContext.Current.CancellationToken)).StatusCode);
        await anonimo.Postar("/cadastro/operadora/codigo", new { nome = "Outra", email }, HttpStatusCode.UnprocessableEntity);
        var auditoria = await operadora.Pegar($"/operadora/auditoria?busca={Uri.EscapeDataString(email)}");
        Assert.Equal("POST /cadastro/operadora", auditoria.GetProperty("registros")[0].GetProperty("acao").GetString());
    }

    [Fact]
    public async Task Sem_a_configuracao_o_cadastro_fica_fechado()
    {
        await using var fabrica = api.Fabrica.WithWebHostBuilder(web => web.UseSetting("Plataforma:CadastroDeOperadoraAberto", "false"));
        var anonimo = fabrica.CreateClient();

        Assert.False((await anonimo.Pegar("/cadastro/operadora")).GetProperty("aberto").GetBoolean());
        await anonimo.Postar("/cadastro/operadora/codigo", new { nome = "Operação", email = $"fechado-{Guid.NewGuid():N}@avaliaone.com.br" },
            HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Email_de_dono_nao_vira_operadora_pelo_cadastro()
    {
        var anonimo = api.Fabrica.CreateClient();
        var (_, email) = await api.CriarRevendedor("Revenda Do Dominio");
        var doDominio = email.Replace("@teste.com.br", "@avaliaone.com.br", StringComparison.Ordinal);
        await api.ClienteLogado(email);
        await anonimo.Postar("/cadastro/operadora/codigo", new { nome = "Dono", email = doDominio }, HttpStatusCode.NoContent);

        await anonimo.Postar("/cadastro/operadora/codigo", new { nome = "Dono", email }, HttpStatusCode.UnprocessableEntity);
    }

    private async Task<string> UltimoCodigo(string email)
    {
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var assunto = await banco.Texto($"select assunto from emails where para = '{email}' and assunto like 'Código de acesso%' order by criado_em desc limit 1");
        return Seis().Match(assunto).Value;
    }

    [GeneratedRegex(@"\d{6}")]
    private static partial Regex Seis();
}
