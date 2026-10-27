using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using AvaliaSales.Tests.Apoio;
using Microsoft.Extensions.DependencyInjection;

namespace AvaliaSales.Tests.Acesso;

[Collection(ColecaoDaApi.Nome)]
public sealed partial class CadastroPublicoTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static object Corpo(string email, string cnpj, string celular = "(11) 98765-4321") =>
        new { empresa = "Revenda Publica", cnpj, celular, endereco = ApiDeTeste.Endereco, nome = "Ana", email };

    private static async Task<(HttpStatusCode Status, string Texto)> Cadastrar(HttpClient cliente, object corpo)
    {
        var resposta = await cliente.PostAsJsonAsync("/cadastro", corpo, Ct);
        // O traceId muda a cada pedido; o resto da resposta tem de ser idêntico.
        var texto = TraceId().Replace(await resposta.Content.ReadAsStringAsync(Ct), "\"traceId\":\"-\"");
        return (resposta.StatusCode, texto);
    }

    [GeneratedRegex("\"traceId\":\"[^\"]*\"")]
    private static partial Regex TraceId();

    /// <summary>O texto do último e-mail, aberto: o link de cadastro cria a senha e fica cifrado na fila.</summary>
    private async Task<string> UltimoEmail(string email)
    {
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        return (await banco.UltimoEmail(email)).Texto;
    }

    private async Task<long> Emails(string email)
    {
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        return await banco.Contar($"select count(*) from emails where para = '{email}'");
    }

    private async Task Envelhecer(string email, string intervalo)
    {
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        await banco.Texto($"""
            with u as (select id from usuarios where email = '{email}'),
            l as (update links_de_senha set criado_em = criado_em - interval '{intervalo}' where usuario_id in (select id from u) returning 1),
            e as (update emails set criado_em = criado_em - interval '{intervalo}' where para = '{email}' returning 1)
            select 1
            """);
    }

    private static string Token(string texto) => Link().Match(texto) is { Success: true } m ? m.Groups[1].Value : throw new Xunit.Sdk.XunitException(texto);

    [GeneratedRegex(@"/confirmar-cadastro#([A-Za-z0-9_-]{20,})")]
    private static partial Regex Link();

    [Fact]
    public async Task Cadastro_cria_pendente_sem_token_e_so_entra_depois_do_link()
    {
        var email = $"novo-{Guid.NewGuid():N}@teste.com.br";
        var cliente = api.Fabrica.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });

        var resposta = await cliente.PostAsJsonAsync("/cadastro", Corpo(email, Cenarios.Cnpj()), Ct);
        var semCelular = await cliente.PostAsJsonAsync("/cadastro",
            new { empresa = "Sem Celular", cnpj = Cenarios.Cnpj(), endereco = ApiDeTeste.Endereco, nome = "Outra", email = "y" + email }, Ct);
        var semEndereco = await cliente.PostAsJsonAsync("/cadastro",
            new { empresa = "Sem Endereço", cnpj = Cenarios.Cnpj(), celular = "(11) 98765-4321", nome = "Outra", email = "z" + email }, Ct);

        Assert.Equal(HttpStatusCode.Accepted, resposta.StatusCode);
        Assert.False(resposta.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, semCelular.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, semEndereco.StatusCode);
        var token = Token(await UltimoEmail(email));
        await cliente.Postar("/auth/entrar", new { email, senha = ApiDeTeste.Senha }, HttpStatusCode.Unauthorized);

        var entrou = await cliente.Postar("/auth/senha", new { token, senha = "senha-escolhida-123" });
        Assert.True(entrou.GetProperty("perfil").GetProperty("emailConfirmado").GetBoolean());
        Assert.Equal("Revenda Publica", entrou.GetProperty("perfil").GetProperty("revendedor").GetString());
        await cliente.Postar("/auth/senha", new { token, senha = "outra-senha-escolhida" }, HttpStatusCode.UnprocessableEntity);
        await cliente.Postar("/auth/entrar", new { email, senha = "senha-escolhida-123" });
    }

    /// <summary>O link de cadastro cria a senha da conta: na fila ele fica cifrado, como o convite e a redefinição.</summary>
    [Fact]
    public async Task Link_de_cadastro_fica_cifrado_na_fila()
    {
        var email = $"cifra-{Guid.NewGuid():N}@teste.com.br";
        var (status, _) = await Cadastrar(api.Fabrica.CreateClient(), Corpo(email, Cenarios.Cnpj()));
        Assert.Equal(HttpStatusCode.Accepted, status);

        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var guardado = await banco.Texto($"select assunto || '|' || texto from emails where para = '{email}'");
        Assert.StartsWith("(conteúdo protegido)|avalia-cifra:v1:", guardado, StringComparison.Ordinal);
        Assert.DoesNotContain("confirmar-cadastro", guardado, StringComparison.Ordinal);
        Assert.Contains("confirmar-cadastro", await UltimoEmail(email), StringComparison.Ordinal);
    }

    /// <summary>O nome vai no e-mail oficial mandado a qualquer endereço digitado: endereço de site nele é recusado.</summary>
    [Theory]
    [InlineData("Confirme: https://mau.exemplo/x", "Revenda Publica")]
    [InlineData("Ana", "Acesse www.mau.exemplo")]
    public async Task Nome_ou_empresa_com_endereco_de_site_e_recusado(string nome, string empresa)
    {
        var email = $"url-{Guid.NewGuid():N}@teste.com.br";
        var resposta = await api.Fabrica.CreateClient().PostAsJsonAsync("/cadastro",
            new { empresa, cnpj = Cenarios.Cnpj(), celular = "(11) 98765-4321", endereco = ApiDeTeste.Endereco, nome, email }, Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(0, await banco.Contar($"select count(*) from emails where para = '{email}'"));
    }

    [Fact]
    public async Task Resposta_e_igual_para_email_e_cnpj_novos_e_ja_cadastrados()
    {
        var (_, emailExistente) = await api.CriarRevendedor("Ja Existe");
        var cliente = api.Fabrica.CreateClient();
        var cnpjNovo = Cenarios.Cnpj();
        var novo = await Cadastrar(cliente, Corpo($"novo-{Guid.NewGuid():N}@teste.com.br", cnpjNovo));
        var emailRepetido = await Cadastrar(cliente, Corpo(emailExistente, Cenarios.Cnpj()));

        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var cnpjDeQuemJaTem = Cenarios.Cnpj();
        await banco.Texto($"update revendedores set cnpj = '{cnpjDeQuemJaTem}' where id = (select revendedor_id from usuarios where email = '{emailExistente}') returning 1");
        var cnpjRepetido = await Cadastrar(cliente, Corpo($"outro-{Guid.NewGuid():N}@teste.com.br", cnpjDeQuemJaTem));

        Assert.Equal(novo, emailRepetido);
        Assert.Equal(novo, cnpjRepetido);
        Assert.DoesNotContain("já", novo.Texto, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await banco.Contar($"select count(*) from usuarios where email = '{emailExistente}'"));
    }

    [Fact]
    public async Task Quem_ja_tem_conta_recebe_o_aviso_com_link_para_entrar_e_recuperar_a_senha()
    {
        var (_, email) = await api.CriarRevendedor("Aviso De Conta");
        var antes = await Emails(email);

        var (status, _) = await Cadastrar(api.Fabrica.CreateClient(), Corpo(email, Cenarios.Cnpj()));

        Assert.Equal(HttpStatusCode.Accepted, status);
        Assert.Equal(antes + 1, await Emails(email));
        var texto = await UltimoEmail(email);
        Assert.Contains("/entrar", texto, StringComparison.Ordinal);
        Assert.Contains("/esqueci", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("confirmar-cadastro", texto, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Erro_de_campo_e_igual_com_e_sem_cadastro_existente()
    {
        var (_, existente) = await api.CriarRevendedor("Campo Igual");
        var cliente = api.Fabrica.CreateClient();

        var comConta = await Cadastrar(cliente, Corpo(existente, Cenarios.Cnpj(), celular: "123"));
        var semConta = await Cadastrar(cliente, Corpo($"novo-{Guid.NewGuid():N}@teste.com.br", Cenarios.Cnpj(), celular: "123"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, comConta.Status);
        Assert.Equal(semConta, comConta);
    }

    [Fact]
    public async Task Novo_cadastro_do_mesmo_email_pendente_espera_um_minuto_e_depois_substitui_o_pendente()
    {
        var email = $"pendente-{Guid.NewGuid():N}@teste.com.br";
        var cliente = api.Fabrica.CreateClient();
        var cnpj = Cenarios.Cnpj();
        await Cadastrar(cliente, Corpo(email, cnpj));
        var primeiro = Token(await UltimoEmail(email));

        var logo = await Cadastrar(cliente, Corpo(email, cnpj));
        Assert.Equal(HttpStatusCode.Accepted, logo.Status);
        Assert.Equal(1, await Emails(email));

        await Envelhecer(email, "2 minutes");
        var depois = await Cadastrar(cliente, Corpo(email, cnpj));
        Assert.Equal(HttpStatusCode.Accepted, depois.Status);
        Assert.Equal(2, await Emails(email));
        var segundo = Token(await UltimoEmail(email));

        await cliente.Postar("/auth/senha", new { token = primeiro, senha = "senha-escolhida-123" }, HttpStatusCode.UnprocessableEntity);
        await cliente.Postar("/auth/senha", new { token = segundo, senha = "senha-escolhida-123" });
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(1, await banco.Contar($"select count(*) from revendedores where cnpj = '{cnpj}'"));
    }

    [Fact]
    public async Task Reenvio_respeita_a_espera_e_responde_igual_para_email_sem_cadastro()
    {
        var email = $"reenvio-{Guid.NewGuid():N}@teste.com.br";
        var cliente = api.Fabrica.CreateClient();
        await Cadastrar(cliente, Corpo(email, Cenarios.Cnpj()));

        await cliente.Postar("/cadastro/reenvio", new { email }, HttpStatusCode.NoContent);
        Assert.Equal(1, await Emails(email));
        await Envelhecer(email, "2 minutes");
        await cliente.Postar("/cadastro/reenvio", new { email }, HttpStatusCode.NoContent);
        Assert.Equal(2, await Emails(email));

        var estranho = $"ninguem-{Guid.NewGuid():N}@teste.com.br";
        await cliente.Postar("/cadastro/reenvio", new { email = estranho }, HttpStatusCode.NoContent);
        Assert.Equal(0, await Emails(estranho));
    }

    [Fact]
    public async Task Limite_por_email_na_hora_descarta_em_silêncio_com_a_mesma_resposta()
    {
        var email = $"limite-{Guid.NewGuid():N}@teste.com.br";
        var cliente = api.Fabrica.CreateClient();
        for (var i = 0; i < 4; i++)
        {
            await Envelhecer(email, "2 minutes");
            var (status, _) = await Cadastrar(cliente, Corpo(email, Cenarios.Cnpj()));
            Assert.Equal(HttpStatusCode.Accepted, status);
        }

        Assert.Equal(3, await Emails(email));
    }

    [Fact]
    public async Task Teto_diario_de_emails_do_cadastro_responde_503_sem_criar_nada()
    {
        using var fabrica = api.Fabrica.WithWebHostBuilder(w => w.UseSetting("Cadastro:EmailsPorDia", "1"));
        var cliente = fabrica.CreateClient();
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        await banco.Texto("update emails set criado_em = criado_em - interval '3 days' where chave like 'cadastro:%' returning 1");

        var primeiro = await Cadastrar(cliente, Corpo($"teto-{Guid.NewGuid():N}@teste.com.br", Cenarios.Cnpj()));
        var cnpj = Cenarios.Cnpj();
        var email = $"teto-{Guid.NewGuid():N}@teste.com.br";
        var segundo = await Cadastrar(cliente, Corpo(email, cnpj));

        Assert.Equal(HttpStatusCode.Accepted, primeiro.Status);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, segundo.Status);
        Assert.Equal(0, await banco.Contar($"select count(*) from revendedores where cnpj = '{cnpj}'"));
    }

    [Fact]
    public async Task Limite_por_ip_responde_429()
    {
        using var fabrica = api.Fabrica.WithWebHostBuilder(w => w.UseSetting("Limites:CadastrosPorHoraPorIp", "2"));
        var cliente = fabrica.CreateClient();

        var respostas = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            respostas.Add((await Cadastrar(cliente, Corpo($"ip-{Guid.NewGuid():N}@teste.com.br", Cenarios.Cnpj()))).Status);
        }

        Assert.Equal([HttpStatusCode.Accepted, HttpStatusCode.Accepted, HttpStatusCode.TooManyRequests], respostas);
    }

    [Fact]
    public async Task Pendente_nao_recupera_senha_e_nao_entra_nem_com_senha_gravada_a_mao()
    {
        var email = $"trava-{Guid.NewGuid():N}@teste.com.br";
        var cliente = api.Fabrica.CreateClient();
        await Cadastrar(cliente, Corpo(email, Cenarios.Cnpj()));
        await cliente.Postar("/auth/senha/link", new { email }, HttpStatusCode.NoContent);
        Assert.Equal(1, await Emails(email));

        using (var escopo = api.Fabrica.Services.CreateScope())
        {
            var usuarios = escopo.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Infrastructure.Identidade.Usuario>>();
            var usuario = (await usuarios.FindByEmailAsync(email))!;
            await usuarios.AddPasswordAsync(usuario, ApiDeTeste.Senha);
        }

        await cliente.Postar("/auth/entrar", new { email, senha = ApiDeTeste.Senha }, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dono_antigo_sem_email_confirmado_continua_entrando()
    {
        var (_, email) = await api.CriarRevendedor("Legado");
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        await banco.Texto($"update usuarios set email_confirmed = false where email = '{email}' returning 1");

        var entrou = await api.Fabrica.CreateClient().Postar("/auth/entrar", new { email, senha = ApiDeTeste.Senha });

        Assert.False(entrou.GetProperty("perfil").GetProperty("emailConfirmado").GetBoolean());
    }

    [Fact]
    public async Task Limite_por_cnpj_descarta_em_silencio_com_a_mesma_resposta()
    {
        using var fabrica = api.Fabrica.WithWebHostBuilder(w => w.UseSetting("Cadastro:TentativasPorCnpjPorHora", "1"));
        var cliente = fabrica.CreateClient();
        var cnpj = Cenarios.Cnpj();
        var primeiro = await Cadastrar(cliente, Corpo($"cnpj-{Guid.NewGuid():N}@teste.com.br", cnpj));
        var outro = $"cnpj-{Guid.NewGuid():N}@teste.com.br";
        var segundo = await Cadastrar(cliente, Corpo(outro, cnpj));

        Assert.Equal(primeiro, segundo);
        Assert.Equal(0, await Emails(outro));
    }

    [Fact]
    public async Task Worker_descarta_o_pendente_vencido_e_libera_email_e_cnpj_sem_tocar_nas_contas_de_verdade()
    {
        var email = $"vencido-{Guid.NewGuid():N}@teste.com.br";
        var recente = $"recente-{Guid.NewGuid():N}@teste.com.br";
        var cliente = api.Fabrica.CreateClient();
        var cnpj = Cenarios.Cnpj();
        await Cadastrar(cliente, Corpo(email, cnpj));
        await Cadastrar(cliente, Corpo(recente, Cenarios.Cnpj()));
        var (_, confirmado) = await api.CriarRevendedor("Conta De Verdade");
        var legado = await api.CriarDonoSemConfirmar("Conta Legada");
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        await banco.Texto($"update revendedores set criado_em = now() - interval '3 days' where id in (select revendedor_id from usuarios where email in ('{email}', '{confirmado}', '{legado}')) returning 1");

        using var worker = api.AmbienteDoWorker(new GatewayFalso(), new AssinaturaFalsa());
        using var escopo = worker.CreateScope();
        var descartados = await escopo.ServiceProvider.GetRequiredService<Infrastructure.Identidade.LimpezaDeCadastros>().Executar(Ct);

        Assert.Equal(1, descartados);
        Assert.Equal(0, await banco.Contar($"select count(*) from usuarios where email = '{email}'"));
        Assert.Equal(0, await banco.Contar($"select count(*) from revendedores where cnpj = '{cnpj}'"));
        Assert.Equal(3, await banco.Contar($"select count(*) from usuarios where email in ('{recente}', '{confirmado}', '{legado}')"));

        // E-mail e CNPJ liberados: um cadastro novo com eles segue o fluxo normal.
        await Envelhecer(email, "2 minutes");
        var (status, _) = await Cadastrar(cliente, Corpo(email, cnpj));
        Assert.Equal(HttpStatusCode.Accepted, status);
        Assert.Equal(1, await banco.Contar($"select count(*) from usuarios where email = '{email}'"));
        Assert.Contains("confirmar-cadastro", await UltimoEmail(email), StringComparison.Ordinal);
    }
}
