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

    /// <summary>Com a sessão roubada não dá para tentar códigos até desligar: cinco erros bloqueiam, e bloqueado nem o código certo passa.</summary>
    [Fact]
    public async Task Desligar_com_cinco_codigos_errados_bloqueia_e_o_certo_nao_passa()
    {
        var (_, email) = await api.CriarRevendedor("Revenda 2FA Força Bruta");
        var dono = await api.ClienteLogado(email);
        var (chave, _) = await Ligar(dono);

        for (var i = 0; i < 5; i++)
        {
            await dono.Postar("/conta/segundo-fator/desligar", new { codigo = $"00000{i}" }, HttpStatusCode.UnprocessableEntity);
        }

        var recusa = await dono.Postar("/conta/segundo-fator/desligar", new { codigo = Totp.Codigo(chave) }, HttpStatusCode.UnprocessableEntity);
        Assert.Contains("Espere", recusa.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.True((await dono.Pegar("/conta/segundo-fator")).GetProperty("ligado").GetBoolean());
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(1, await banco.Contar($"select count(*) from usuarios where email = '{email}' and lockout_end > now()"));
    }

    [Fact]
    public async Task Ligar_conta_o_codigo_errado_e_o_certo_zera_a_contagem()
    {
        var (_, email) = await api.CriarRevendedor("Revenda 2FA Ligar Errado");
        var dono = await api.ClienteLogado(email);
        var chave = (await dono.Postar("/conta/segundo-fator/chave", new { })).GetProperty("chave").GetString()!;
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);

        await dono.Postar("/conta/segundo-fator", new { codigo = "000000" }, HttpStatusCode.UnprocessableEntity);
        Assert.Equal(1, await banco.Contar($"select access_failed_count from usuarios where email = '{email}'"));

        await dono.Postar("/conta/segundo-fator", new { codigo = Totp.Codigo(chave) });
        Assert.Equal(0, await banco.Contar($"select access_failed_count from usuarios where email = '{email}'"));
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
        var codigo = SeisDigitos().Match(assunto).Value;

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

    /// <summary>Quem perdeu o app e os códigos recupera a conta pelo suporte: o comando desliga o segundo fator e avisa o e-mail.</summary>
    [Fact]
    public async Task Suporte_desliga_o_segundo_fator_pela_linha_de_comando_e_a_senha_volta_a_entrar()
    {
        var (_, email) = await api.CriarRevendedor("Revenda 2FA Perdido");
        await Ligar(await api.ClienteLogado(email));

        var saida = await AvaliaSales.Api.NovoRevendedor.DesligarSegundoFator(api.Fabrica.Services, ["--desligar-segundo-fator", $"--email={email}", "--motivo=celular perdido"]);

        Assert.Equal(0, saida);
        var entrada = await api.Fabrica.CreateClient().PostAsJsonAsync("/auth/entrar", new { email, senha = ApiDeTeste.Senha }, Ct);
        Assert.Equal(HttpStatusCode.OK, entrada.StatusCode);
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Contains(await banco.Emails(email), e => e.Assunto == "Segundo fator desligado pelo suporte");
        Assert.Equal(1, await AvaliaSales.Api.NovoRevendedor.DesligarSegundoFator(api.Fabrica.Services, ["--desligar-segundo-fator", $"--email={email}", "--motivo=de novo"]));
    }

    /// <summary>Quem entrou antes só com a senha não segue renovando o acesso depois que o dono liga o segundo fator; o aparelho de quem ligou continua.</summary>
    [Fact]
    public async Task Ligar_o_segundo_fator_encerra_as_outras_sessoes_e_mantem_a_de_quem_ligou()
    {
        var (_, email) = await api.CriarRevendedor("Revenda 2FA Sessões");
        var esteAparelho = await api.ClienteLogado(email);
        var outroAparelho = await api.ClienteLogado(email);

        await Ligar(esteAparelho);

        Assert.Equal(HttpStatusCode.OK, (await esteAparelho.PostAsync("/auth/renovar", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await outroAparelho.PostAsync("/auth/renovar", null, Ct)).StatusCode);
    }

    /// <summary>A operadora não tem revenda: ligar e desligar o segundo fator dela vão à trilha da operadora.</summary>
    [Fact]
    public async Task Ligar_e_desligar_o_segundo_fator_da_operadora_vao_a_trilha_da_operadora()
    {
        var email = await api.CriarOperadora();
        var operadora = await api.ClienteLogado(email);
        var (chave, _) = await Ligar(operadora);
        await operadora.Postar("/conta/segundo-fator/desligar", new { codigo = Totp.Codigo(chave) }, HttpStatusCode.NoContent);

        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var id = await banco.Texto($"select id::text from usuarios where email = '{email}'");
        Assert.Equal(1, await banco.Contar($"select count(*) from auditoria_da_operadora where usuario_id = '{id}' and acao = 'Segundo fator ligado' and corpo like '%app autenticador%'"));
        Assert.Equal(1, await banco.Contar($"select count(*) from auditoria_da_operadora where usuario_id = '{id}' and acao = 'Segundo fator desligado'"));
    }

    /// <summary>O desligamento pelo suporte fica registrado com o motivo: na auditoria da revenda para o dono, na trilha da operadora para a operadora.</summary>
    [Fact]
    public async Task Suporte_desligar_o_segundo_fator_fica_na_auditoria_com_o_motivo()
    {
        var (revendedor, emailDoDono) = await api.CriarRevendedor("Revenda 2FA Suporte Auditado");
        await Ligar(await api.ClienteLogado(emailDoDono));
        var emailDaOperadora = await api.CriarOperadora();
        var operadora = await api.ClienteLogado(emailDaOperadora);
        await Ligar(operadora);

        Assert.Equal(0, await AvaliaSales.Api.NovoRevendedor.DesligarSegundoFator(api.Fabrica.Services, ["--desligar-segundo-fator", $"--email={emailDoDono}", "--motivo=celular roubado"]));
        Assert.Equal(0, await AvaliaSales.Api.NovoRevendedor.DesligarSegundoFator(api.Fabrica.Services, ["--desligar-segundo-fator", $"--email={emailDaOperadora}", "--motivo=app apagado"]));

        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var idDaOperadora = await banco.Texto($"select id::text from usuarios where email = '{emailDaOperadora}'");
        Assert.Equal(1, await banco.Contar($"select count(*) from auditoria where revendedor_id = '{revendedor}' and acao = 'SegundoFatorDesligado' and depois like '%celular roubado%'"));
        Assert.Equal(1, await banco.Contar($"select count(*) from auditoria_da_operadora where usuario_id = '{idDaOperadora}' and acao = 'Segundo fator desligado pelo suporte' and corpo like '%app apagado%'"));
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

    [GeneratedRegex(@"\d{6}")]
    private static partial Regex SeisDigitos();
}
