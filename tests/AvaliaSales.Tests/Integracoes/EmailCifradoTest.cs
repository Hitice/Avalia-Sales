using System.Security.Cryptography;
using AvaliaSales.Application.Avisos;
using AvaliaSales.Domain.Avisos;
using AvaliaSales.Infrastructure.Banco;
using AvaliaSales.Infrastructure.Integracoes;
using AvaliaSales.Infrastructure.Seguranca;
using AvaliaSales.Tests.Apoio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace AvaliaSales.Tests.Integracoes;

/// <summary>E-mail sensível cifrado em repouso na fila: o banco não vê o link nem o código, o Resend recebe tudo.</summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class EmailCifradoTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class EnvioFalso : IEnvioDeEmail
    {
        public bool Configurado => true;
        public List<MensagemDeEmail> Enviadas { get; } = [];

        public Task<string> Enviar(MensagemDeEmail mensagem, CancellationToken ct)
        {
            Enviadas.Add(mensagem);
            return Task.FromResult("em_" + Enviadas.Count);
        }
    }

    private static FakeTimeProvider RelogioAntigo() => new(new DateTimeOffset(2001, 1, 1, 12, 0, 0, TimeSpan.Zero));

    private static string Chave() => "teste:" + Guid.NewGuid().ToString("N");

    private static string NovaChaveDeCifra() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static async Task Enfileirar(ServiceProvider worker, string chave, string assunto, string texto)
    {
        using var escopo = worker.CreateScope();
        escopo.ServiceProvider.GetRequiredService<FilaDeEmails>().Enfileirar(chave, "ana@teste.com.br", assunto, texto, sensivel: true);
        await escopo.ServiceProvider.GetRequiredService<BancoDeDados>().SaveChangesAsync(Ct);
    }

    private static async Task Enviar(ServiceProvider worker)
    {
        using var escopo = worker.CreateScope();
        await escopo.ServiceProvider.GetRequiredService<EnvioDeEmails>().Executar(50, Ct);
    }

    [Fact]
    public async Task Email_sensivel_fica_cifrado_na_fila_o_resend_recebe_o_conteudo_e_o_envio_limpa_o_banco()
    {
        var envio = new EnvioFalso();
        await using var worker = api.AmbienteDoWorker(api.Gateway, new AssinaturaFalsa(), RelogioAntigo(), email: envio);
        var chave = Chave();
        await Enfileirar(worker, chave, "Código de acesso: 482913", "Link: https://painel/senha#segredo-do-link");

        await using var dono = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var guardado = await dono.Texto($"select assunto || '|' || texto from emails where chave = '{chave}'");
        Assert.DoesNotContain("482913", guardado, StringComparison.Ordinal);
        Assert.DoesNotContain("segredo-do-link", guardado, StringComparison.Ordinal);
        Assert.StartsWith("(conteúdo protegido)|avalia-cifra:v1:", guardado, StringComparison.Ordinal);

        await Enviar(worker);

        var enviada = Assert.Single(envio.Enviadas, m => m.Chave == chave);
        Assert.Equal("Código de acesso: 482913", enviada.Assunto);
        Assert.Equal("Link: https://painel/senha#segredo-do-link", enviada.Texto);
        Assert.Equal("(conteúdo removido)", await dono.Texto($"select assunto from emails where chave = '{chave}'"));
        Assert.Equal("", await dono.Texto($"select texto from emails where chave = '{chave}'"));
    }

    [Fact]
    public async Task Mesmo_texto_em_duas_linhas_gera_valores_diferentes()
    {
        await using var worker = api.AmbienteDoWorker(api.Gateway, new AssinaturaFalsa(), RelogioAntigo(), email: new EnvioFalso());
        var a = Chave();
        var b = Chave();
        await Enfileirar(worker, a, "Código: 111111", "Texto igual");
        await Enfileirar(worker, b, "Código: 111111", "Texto igual");

        await using var dono = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.NotEqual(await dono.Texto($"select texto from emails where chave = '{a}'"), await dono.Texto($"select texto from emails where chave = '{b}'"));
    }

    [Fact]
    public async Task Linha_antiga_em_claro_ainda_envia()
    {
        var envio = new EnvioFalso();
        var relogio = RelogioAntigo();
        await using var worker = api.AmbienteDoWorker(api.Gateway, new AssinaturaFalsa(), relogio, email: envio);
        var chave = Chave();
        using (var escopo = worker.CreateScope())
        {
            var banco = escopo.ServiceProvider.GetRequiredService<BancoDeDados>();
            banco.Emails.Add(new EmailNaFila(chave, "ana@teste.com.br", "Código de acesso: 777777", "Link antigo em claro", relogio.GetUtcNow(), sensivel: true));
            await banco.SaveChangesAsync(Ct);
        }

        await Enviar(worker);

        var enviada = Assert.Single(envio.Enviadas, m => m.Chave == chave);
        Assert.Equal("Código de acesso: 777777", enviada.Assunto);
        Assert.Equal("Link antigo em claro", enviada.Texto);
    }

    [Fact]
    public async Task Chave_errada_vira_falha_definitiva_sem_vazar_o_conteudo_nem_chamar_o_resend()
    {
        var chave = Chave();
        await using (var api1 = api.AmbienteDoWorker(api.Gateway, new AssinaturaFalsa(), RelogioAntigo(), email: new EnvioFalso(),
            configuracaoExtra: [new("Seguranca:ChaveDosEmails", NovaChaveDeCifra())]))
        {
            await Enfileirar(api1, chave, "Código de acesso: 555555", "Link: segredo-que-nao-pode-vazar");
        }

        var envio = new EnvioFalso();
        await using var outroWorker = api.AmbienteDoWorker(api.Gateway, new AssinaturaFalsa(), RelogioAntigo(), email: envio,
            configuracaoExtra: [new("Seguranca:ChaveDosEmails", NovaChaveDeCifra())]);
        await Enviar(outroWorker);

        Assert.DoesNotContain(envio.Enviadas, m => m.Chave == chave);
        await using var dono = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(EmailNaFila.TentativasMaximas.ToString(System.Globalization.CultureInfo.InvariantCulture), await dono.Texto($"select tentativas from emails where chave = '{chave}'"));
        Assert.Equal("", await dono.Texto($"select texto from emails where chave = '{chave}'"));
        var erro = await dono.Texto($"select ultimo_erro from emails where chave = '{chave}'");
        Assert.DoesNotContain("555555", erro, StringComparison.Ordinal);
        Assert.DoesNotContain("segredo-que-nao-pode-vazar", erro, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rotacao_a_chave_anterior_ainda_abre_o_que_a_antiga_cifrou()
    {
        var antiga = NovaChaveDeCifra();
        var chave = Chave();
        await using (var antes = api.AmbienteDoWorker(api.Gateway, new AssinaturaFalsa(), RelogioAntigo(), email: new EnvioFalso(),
            configuracaoExtra: [new("Seguranca:ChaveDosEmails", antiga)]))
        {
            await Enfileirar(antes, chave, "Código: 909090", "Texto da chave antiga");
        }

        var envio = new EnvioFalso();
        await using var depois = api.AmbienteDoWorker(api.Gateway, new AssinaturaFalsa(), RelogioAntigo(), email: envio,
            configuracaoExtra: [new("Seguranca:ChaveDosEmails", NovaChaveDeCifra()), new("Seguranca:ChaveAnteriorDosEmails", antiga)]);
        await Enviar(depois);

        Assert.Equal("Texto da chave antiga", Assert.Single(envio.Enviadas, m => m.Chave == chave).Texto);
    }

    [Fact]
    public void Linha_cifrada_nao_abre_sob_outra_chave_de_email()
    {
        var cifra = new CifraDeEmails(Options.Create(new OpcoesDeSeguranca { ChaveDosEmails = NovaChaveDeCifra() }));
        var valor = cifra.Cifrar("chave:a", "Assunto", "Texto");

        Assert.Equal(("Assunto", "Texto"), cifra.Abrir("chave:a", valor));
        Assert.Throws<CifraDeEmailInvalidaException>(() => cifra.Abrir("chave:b", valor));
        Assert.Throws<CifraDeEmailInvalidaException>(() => cifra.Abrir("chave:a", valor[..^4] + "AAAA"));
    }

    private sealed class Ambiente(string nome) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = nome;
        public string ApplicationName { get; set; } = "teste";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    [Theory]
    [InlineData("Production", null, false)]
    [InlineData("Production", "curta", false)]
    [InlineData("Development", null, true)]
    public void A_subida_falha_fechada_fora_do_development_sem_chave_valida(string ambiente, string? chave, bool valida)
    {
        var servicos = new ServiceCollection().AddSingleton<IHostEnvironment>(new Ambiente(ambiente)).BuildServiceProvider();
        var resultado = new ValidadorDasOpcoesDeSeguranca(servicos).Validate(null, new OpcoesDeSeguranca { ChaveDosEmails = chave });
        Assert.Equal(valida, resultado.Succeeded);
    }

    [Fact]
    public void A_subida_aceita_chave_de_32_bytes_em_producao()
    {
        var servicos = new ServiceCollection().AddSingleton<IHostEnvironment>(new Ambiente("Production")).BuildServiceProvider();
        Assert.True(new ValidadorDasOpcoesDeSeguranca(servicos).Validate(null, new OpcoesDeSeguranca { ChaveDosEmails = NovaChaveDeCifra() }).Succeeded);
    }
}
