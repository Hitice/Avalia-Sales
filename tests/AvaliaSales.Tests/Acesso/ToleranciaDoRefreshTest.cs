using System.Net;
using System.Net.Http.Json;
using AvaliaSales.Infrastructure.Identidade;
using AvaliaSales.Tests.Apoio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace AvaliaSales.Tests.Acesso;

/// <summary>
/// Duas abas (ou uma retentativa) com o mesmo refresh token em até 10 segundos
/// recebem sessão válida; o reuso fora da janela continua encerrando a família.
/// </summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class ToleranciaDoRefreshTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Cookie(HttpResponseMessage resposta) =>
        resposta.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("avalia_sessao=", StringComparison.Ordinal)).Split(';')[0];

    private static HttpRequestMessage Renovar(string cookie)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Post, "/auth/renovar");
        pedido.Headers.Add("Cookie", cookie);
        return pedido;
    }

    private HttpClient Cliente() => api.Fabrica.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });

    [Fact]
    public async Task Duas_renovacoes_simultaneas_com_o_mesmo_refresh_recebem_sessao_valida()
    {
        var (_, email) = await api.CriarRevendedor("Revenda da Corrida do Refresh");
        var cliente = Cliente();
        var primeiro = Cookie(await cliente.PostAsJsonAsync("/auth/entrar", new { email, senha = ApiDeTeste.Senha }, Ct));

        var respostas = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => cliente.SendAsync(Renovar(primeiro), Ct)));

        Assert.All(respostas, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        // Qualquer um dos cookies emitidos mantém a sessão: o navegador fica com o último que processar.
        foreach (var resposta in respostas)
        {
            Assert.Equal(HttpStatusCode.OK, (await cliente.SendAsync(Renovar(Cookie(resposta)), Ct)).StatusCode);
        }
    }

    [Fact]
    public async Task Reenvio_dentro_da_janela_nao_derruba_o_cookie_novo()
    {
        var (_, email) = await api.CriarRevendedor("Revenda do Reenvio");
        var cliente = Cliente();
        var primeiro = Cookie(await cliente.PostAsJsonAsync("/auth/entrar", new { email, senha = ApiDeTeste.Senha }, Ct));
        var segundo = Cookie(await cliente.SendAsync(Renovar(primeiro), Ct));

        var reenvio = await cliente.SendAsync(Renovar(primeiro), Ct);

        Assert.Equal(HttpStatusCode.OK, reenvio.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cliente.SendAsync(Renovar(segundo), Ct)).StatusCode);
    }

    private async Task<Ambiente> Gestor(string nome)
    {
        var (_, email) = await api.CriarRevendedor(nome);
        var relogio = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var usuario = Guid.Parse(await banco.Texto($"select id::text from usuarios where email = '{email}'"));
        return new Ambiente(api.AmbienteDoWorker(api.Gateway, new AssinaturaFalsa(), relogio), relogio, usuario);
    }

    /// <summary>Cada chamada num escopo novo, como cada requisição: o contexto de uma não guarda a sessão da outra.</summary>
    private sealed record Ambiente(ServiceProvider Worker, FakeTimeProvider Relogio, Guid Usuario) : IAsyncDisposable
    {
        public Task<string> Abrir() => Com(g => g.Abrir(Usuario, Ct));

        public Task<(Usuario Usuario, string Token)?> Renovar(string token) => Com(g => g.Renovar(token, Ct));

        public Task<int> Encerrar(string token) => Com(async g =>
        {
            await g.Encerrar(token, Ct);
            return 0;
        });

        private async Task<T> Com<T>(Func<GestorDeSessoes, Task<T>> acao)
        {
            using var escopo = Worker.CreateScope();
            return await acao(escopo.ServiceProvider.GetRequiredService<GestorDeSessoes>());
        }

        public ValueTask DisposeAsync() => Worker.DisposeAsync();
    }

    [Fact]
    public async Task Reuso_depois_da_janela_encerra_a_familia_inteira()
    {
        await using var ambiente = await Gestor("Revenda do Reuso Tardio");
        var a = await ambiente.Abrir();
        var b = (await ambiente.Renovar(a))!.Value.Token;

        ambiente.Relogio.Advance(GestorDeSessoes.JanelaDeTolerancia + TimeSpan.FromSeconds(1));
        var reuso = await ambiente.Renovar(a);
        var legitimo = await ambiente.Renovar(b);

        Assert.Null(reuso);
        Assert.Null(legitimo);
    }

    [Fact]
    public async Task Reuso_dentro_da_janela_emite_sessao_e_mantem_a_que_ja_existia()
    {
        await using var ambiente = await Gestor("Revenda do Reuso Cedo");
        var a = await ambiente.Abrir();
        var b = (await ambiente.Renovar(a))!.Value.Token;

        ambiente.Relogio.Advance(TimeSpan.FromSeconds(5));
        var reenvio = await ambiente.Renovar(a);

        Assert.NotNull(reenvio);
        Assert.NotEqual(b, reenvio.Value.Token);
        Assert.NotNull(await ambiente.Renovar(b));
    }

    [Fact]
    public async Task Reenvios_em_excesso_dentro_da_janela_viram_reuso()
    {
        await using var ambiente = await Gestor("Revenda do Excesso de Reenvios");
        var a = await ambiente.Abrir();
        await ambiente.Renovar(a);

        for (var i = 0; i < GestorDeSessoes.ReenviosToleradosPorToken; i++)
        {
            Assert.NotNull(await ambiente.Renovar(a));
        }

        Assert.Null(await ambiente.Renovar(a));
    }

    [Fact]
    public async Task Token_de_sessao_encerrada_pelo_sair_nao_ganha_tolerancia()
    {
        await using var ambiente = await Gestor("Revenda do Sair e Reusar");
        var a = await ambiente.Abrir();
        var b = (await ambiente.Renovar(a))!.Value.Token;
        await ambiente.Encerrar(b);

        Assert.Null(await ambiente.Renovar(a));
        Assert.Null(await ambiente.Renovar(b));
    }

    [Fact]
    public async Task Reuso_tardio_de_um_login_nao_afeta_outro_login_do_mesmo_usuario()
    {
        await using var ambiente = await Gestor("Revenda de Dois Logins");
        var celular = await ambiente.Abrir();
        var computador = await ambiente.Abrir();
        await ambiente.Renovar(celular);
        ambiente.Relogio.Advance(TimeSpan.FromMinutes(1));

        Assert.Null(await ambiente.Renovar(celular));
        Assert.NotNull(await ambiente.Renovar(computador));
    }
}
