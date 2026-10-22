using System.Net.Http.Json;
using AvaliaSales.Infrastructure.Identidade;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Acesso;

[Collection(ColecaoDaApi.Nome)]
public sealed class AparelhoNovoTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Primeiro_aparelho_entra_calado_o_mesmo_volta_calado_e_o_novo_gera_aviso()
    {
        var (_, email) = await api.CriarRevendedor("Revenda Aparelhos");
        var celular = api.Fabrica.CreateClient();
        celular.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 Chrome/141.0 Mobile Safari/537.36");

        await Entrar(celular, email);
        await Entrar(celular, email);
        Assert.Equal(0, await Avisos(email));

        var outro = api.Fabrica.CreateClient();
        outro.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Firefox/140.0");
        await Entrar(outro, email);

        Assert.Equal(1, await Avisos(email));
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Contains("Firefox no Windows", await banco.Texto($"select texto from emails where para = '{email}' and assunto = 'Acesso de um aparelho novo'"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 Version/18.0 Mobile/15E148 Safari/604.1", "Safari no iPhone")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 Chrome/141.0 Safari/537.36 Edg/141.0", "Edge no Mac")]
    [InlineData("", "Aparelho desconhecido")]
    public void Descreve_o_aparelho_do_jeito_que_o_dono_reconhece(string agente, string esperado) =>
        Assert.Equal(esperado, GestorDeAparelhos.Descrever(agente));

    private static async Task Entrar(HttpClient cliente, string email)
    {
        var resposta = await cliente.PostAsJsonAsync("/auth/entrar", new { email, senha = ApiDeTeste.Senha }, Ct);
        resposta.EnsureSuccessStatusCode();
    }

    private async Task<long> Avisos(string email)
    {
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        return await banco.Contar($"select count(*) from emails where para = '{email}' and assunto = 'Acesso de um aparelho novo'");
    }
}
