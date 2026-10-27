using System.Net;
using AvaliaSales.Application;
using AvaliaSales.Infrastructure.Impressao;
using AvaliaSales.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AvaliaSales.Tests.Worker;

/// <summary>
/// O tempo esgotado de um HttpClient chega como <see cref="TaskCanceledException"/>, que é um
/// <see cref="OperationCanceledException"/>. Sem a parada pedida, ele é falha da rotina: o Worker segue
/// para a próxima, e o conversor de PDF devolve a falha como integração fora do ar.
/// </summary>
public sealed class QuedaDoConversorTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Um Gotenberg que não responde: a requisição só termina quando o HttpClient desiste.</summary>
    private sealed class SemResposta : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private static ConversorGotenberg Conversor() =>
        new(new HttpClient(new SemResposta()) { BaseAddress = new Uri("http://gotenberg:3000/"), Timeout = TimeSpan.FromMilliseconds(100) });

    private static readonly ArquivoDoDocumento[] Documento = [new("index.html", "<html></html>"u8.ToArray())];

    [Fact]
    public async Task Conversor_sem_resposta_vira_falha_de_integracao_e_nao_cancelamento()
    {
        var erro = await Assert.ThrowsAsync<IntegracaoException>(() => Conversor().Converter(Documento, Ct));

        Assert.Contains("tempo esgotado", erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Conversor_respeita_a_parada_pedida()
    {
        using var parada = new CancellationTokenSource();
        await parada.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Conversor().Converter(Documento, parada.Token));
    }

    [Fact]
    public async Task Tempo_esgotado_numa_rotina_nao_derruba_o_Worker_e_a_parada_pedida_sim()
    {
        await using var servicos = new ServiceCollection().BuildServiceProvider();
        var rotinas = new Rotinas(servicos.GetRequiredService<IServiceScopeFactory>(), NullLogger<Rotinas>.Instance);

        Assert.Equal(0, await rotinas.Rodar("retomadas", (_, _) => throw new TaskCanceledException("tempo do HttpClient"), Ct));

        using var parada = new CancellationTokenSource();
        await parada.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rotinas.Rodar("retomadas", (_, ct) => Task.FromCanceled<int>(ct), parada.Token));
    }
}
