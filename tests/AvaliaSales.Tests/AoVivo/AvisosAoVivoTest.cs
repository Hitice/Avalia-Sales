using System.Net;
using System.Net.Http.Json;
using AvaliaSales.Api.Autenticacao;
using AvaliaSales.Infrastructure.Identidade;
using AvaliaSales.Tests.Apoio;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace AvaliaSales.Tests.AoVivo;

/// <summary>O aviso ao vivo sai do Postgres (NOTIFY) e chega só ao grupo do revendedor certo.</summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class AvisosAoVivoTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(HubConnection Conexao, Func<string, Task<bool>> Esperar)> Conectar(string email)
    {
        var entrada = await api.Fabrica.CreateClient().PostAsJsonAsync("/auth/entrar", new { email, senha = ApiDeTeste.Senha }, Ct);
        var token = (await entrada.Content.ReadFromJsonAsync<ApiDeTeste.TokenLido>(Ct))!.Token;
        var recebidos = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var conexao = new HubConnectionBuilder()
            .WithUrl(new Uri(api.Fabrica.Server.BaseAddress, "hubs/avisos"), o =>
            {
                o.HttpMessageHandlerFactory = _ => api.Fabrica.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
                o.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();
        conexao.On<Aviso>("aviso", a => recebidos.Enqueue(a.Tipo));
        await conexao.StartAsync(Ct);

        async Task<bool> Esperar(string tipo)
        {
            for (var i = 0; i < 50; i++)
            {
                if (recebidos.Contains(tipo))
                {
                    return true;
                }

                await Task.Delay(100, Ct);
            }

            return false;
        }

        return (conexao, Esperar);
    }

    private sealed record Aviso(string Tipo);

    [Fact]
    public async Task Venda_avisa_o_dono_da_revenda_e_nao_avisa_outra()
    {
        var a = await api.RevendaComEstoque("Revenda Ao Vivo");
        var (_, emailB) = await api.CriarRevendedor("Revenda Que Não Ouve");
        var emailDonoA = await DonoDe(a.Id);
        var (conexaoA, esperarA) = await Conectar(emailDonoA);
        var (conexaoB, esperarB) = await Conectar(emailB);

        await a.Dono.Postar("/vendas", Cenarios.Venda(a.Codigos[0], a.ProdutoId, "11955550001"), HttpStatusCode.Created);

        Assert.True(await esperarA("venda"));
        Assert.False(await esperarB("venda"));
        await conexaoA.DisposeAsync();
        await conexaoB.DisposeAsync();
    }

    [Fact]
    public async Task Hub_sem_token_recusa_a_conexao()
    {
        var conexao = new HubConnectionBuilder()
            .WithUrl(new Uri(api.Fabrica.Server.BaseAddress, "hubs/avisos"), o =>
            {
                o.HttpMessageHandlerFactory = _ => api.Fabrica.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .Build();

        var erro = await Assert.ThrowsAsync<HttpRequestException>(() => conexao.StartAsync(Ct));
        Assert.Equal(HttpStatusCode.Unauthorized, erro.StatusCode);
    }

    /// <summary>
    /// A conexão ao vivo cai quando o token vence (ADR 0006). O token sai pelo emissor da API com o relógio recuado, para vencer em
    /// poucos segundos; o relógio do servidor é o real, então o teste espera esses segundos.
    /// </summary>
    [Fact]
    public async Task Conexao_ao_vivo_cai_quando_o_token_vence()
    {
        var (_, email) = await api.CriarRevendedor("Revenda do Token Vencido");
        var opcoes = api.Fabrica.Services.GetRequiredService<IOptions<OpcoesJwt>>();
        string token;
        using (var escopo = api.Fabrica.Services.CreateScope())
        {
            var usuario = (await escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>().FindByEmailAsync(email))!;
            var recuado = new FakeTimeProvider(DateTimeOffset.UtcNow.AddMinutes(-opcoes.Value.MinutosDeValidade).AddSeconds(3));
            token = new EmissorDeToken(opcoes, recuado).Emitir(usuario, null).Token;
        }

        var caiu = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var conexao = new HubConnectionBuilder()
            .WithUrl(new Uri(api.Fabrica.Server.BaseAddress, "hubs/avisos"), o =>
            {
                o.HttpMessageHandlerFactory = _ => api.Fabrica.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
                o.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();
        conexao.Closed += _ =>
        {
            caiu.TrySetResult();
            return Task.CompletedTask;
        };
        await conexao.StartAsync(Ct);

        var primeiro = await Task.WhenAny(caiu.Task, Task.Delay(TimeSpan.FromSeconds(20), Ct));

        Assert.Same(caiu.Task, primeiro);
        await conexao.DisposeAsync();
    }

    private async Task<string> DonoDe(Guid revendedorId)
    {
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        return await banco.Texto($"select email from usuarios where revendedor_id = '{revendedorId}' and papel = 'Dono' limit 1");
    }
}
