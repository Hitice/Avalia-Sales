using System.Net;
using System.Net.Http.Json;
using AvaliaSales.Application.Seguranca;
using AvaliaSales.Tests.Apoio;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace AvaliaSales.Tests.Acesso;

[Collection(ColecaoDaApi.Nome)]
public sealed class CadastroComCaptchaTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class CaptchaFalso(ResultadoDoCaptcha resultado) : IVerificadorDeCaptcha
    {
        public bool Ligado => true;

        public List<string?> Tokens { get; } = [];

        public Task<ResultadoDoCaptcha> Verificar(string? token, string? ip, CancellationToken ct)
        {
            Tokens.Add(token);
            return Task.FromResult(resultado);
        }
    }

    private HttpClient Cliente(CaptchaFalso captcha) =>
        api.Fabrica.WithWebHostBuilder(w => w.ConfigureTestServices(s => s.AddSingleton<IVerificadorDeCaptcha>(captcha))).CreateClient();

    private static object Corpo(string email, string cnpj, string? token = "token-do-widget") =>
        new { empresa = "Revenda Captcha", cnpj, celular = "(11) 98765-4321", endereco = ApiDeTeste.Endereco, nome = "Ana", email, tokenDoCaptcha = token };

    [Fact]
    public async Task Captcha_aprovado_deixa_cadastrar_e_o_token_vai_ao_verificador()
    {
        var captcha = new CaptchaFalso(ResultadoDoCaptcha.Aprovado);
        var email = $"captcha-{Guid.NewGuid():N}@teste.com.br";

        var resposta = await Cliente(captcha).PostAsJsonAsync("/cadastro", Corpo(email, Cenarios.Cnpj()), Ct);

        Assert.Equal(HttpStatusCode.Accepted, resposta.StatusCode);
        Assert.Equal(["token-do-widget"], captcha.Tokens);
    }

    [Fact]
    public async Task Captcha_reprovado_recusa_com_422_e_nao_cria_nada()
    {
        var email = $"captcha-{Guid.NewGuid():N}@teste.com.br";

        var resposta = await Cliente(new CaptchaFalso(ResultadoDoCaptcha.Reprovado)).PostAsJsonAsync("/cadastro", Corpo(email, Cenarios.Cnpj(), token: null), Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(0, await banco.Contar($"select count(*) from usuarios where email = '{email}'"));
        Assert.Equal(0, await banco.Contar($"select count(*) from emails where para = '{email}'"));
    }

    [Fact]
    public async Task Verificador_indisponivel_com_captcha_ligado_recusa_com_503_e_nao_cria_nada()
    {
        var email = $"captcha-{Guid.NewGuid():N}@teste.com.br";

        var resposta = await Cliente(new CaptchaFalso(ResultadoDoCaptcha.Indisponivel)).PostAsJsonAsync("/cadastro", Corpo(email, Cenarios.Cnpj()), Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, resposta.StatusCode);
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(0, await banco.Contar($"select count(*) from usuarios where email = '{email}'"));
    }

    [Fact]
    public async Task Sem_chave_configurada_o_cadastro_nao_pede_captcha()
    {
        var verificador = api.Fabrica.Services.GetRequiredService<IVerificadorDeCaptcha>();

        Assert.False(verificador.Ligado);
        var resposta = await api.Fabrica.CreateClient().PostAsJsonAsync("/cadastro", Corpo($"semcaptcha-{Guid.NewGuid():N}@teste.com.br", Cenarios.Cnpj(), token: null), Ct);
        Assert.Equal(HttpStatusCode.Accepted, resposta.StatusCode);
    }
}
