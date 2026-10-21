using System.Text.Json;
using AvaliaSales.Infrastructure.Banco;
using Microsoft.AspNetCore.SignalR;
using Npgsql;

namespace AvaliaSales.Api.AoVivo;

/// <summary>
/// Escuta o canal do Postgres (LISTEN) e repassa cada aviso ao grupo certo do
/// SignalR. O Worker e a API avisam pelo mesmo canal, então o pagamento
/// confirmado pelo Worker chega à tela aberta na API. Conexão caiu, tenta de
/// novo com espera crescente; perder um aviso só atrasa a tela até a próxima
/// consulta dela.
/// </summary>
public sealed partial class OuvinteDeAvisos(IConfiguration configuracao, IHubContext<HubDeAvisos> hub, ILogger<OuvinteDeAvisos> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var espera = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var conexao = new NpgsqlConnection(configuracao.GetConnectionString("Banco"));
                await conexao.OpenAsync(stoppingToken);
                conexao.Notification += (_, evento) => _ = Repassar(evento.Payload);
                await using (var comando = new NpgsqlCommand($"LISTEN {BancoDeDados.CanalDeAvisos}", conexao))
                {
                    await comando.ExecuteNonQueryAsync(stoppingToken);
                }

                espera = TimeSpan.FromSeconds(1);
                while (!stoppingToken.IsCancellationRequested)
                {
                    await conexao.WaitAsync(stoppingToken);
                }
            }
            catch (Exception erro) when (erro is NpgsqlException or InvalidOperationException or System.Net.Sockets.SocketException)
            {
                LogQueda(log, espera, erro);
                await Task.Delay(espera, stoppingToken);
                espera = TimeSpan.FromSeconds(Math.Min(espera.TotalSeconds * 2, 60));
            }
        }
    }

    private async Task Repassar(string json)
    {
        try
        {
            var aviso = JsonSerializer.Deserialize<AvisoAoVivo>(json);
            if (aviso is null)
            {
                return;
            }

            var conteudo = new { aviso.Tipo };
            if (aviso.RevendedorId is { } revendedor)
            {
                await hub.Clients.Group(HubDeAvisos.GrupoDoRevendedor(revendedor)).SendAsync(HubDeAvisos.Mensagem, conteudo);
            }

            if (aviso.Operadora)
            {
                await hub.Clients.Group(HubDeAvisos.GrupoDaOperadora).SendAsync(HubDeAvisos.Mensagem, conteudo);
            }
        }
        catch (JsonException erro)
        {
            LogAvisoInvalido(log, erro);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Escuta dos avisos ao vivo caiu; nova tentativa em {Espera}")]
    private static partial void LogQueda(ILogger log, TimeSpan espera, Exception erro);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Aviso ao vivo ilegível descartado")]
    private static partial void LogAvisoInvalido(ILogger log, Exception erro);
}
