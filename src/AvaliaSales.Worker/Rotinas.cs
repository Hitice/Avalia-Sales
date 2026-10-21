using AvaliaSales.Infrastructure.Integracoes;

namespace AvaliaSales.Worker;

/// <summary>
/// A cada volta: processa a caixa de entrada dos webhooks, retoma o que
/// ficou pela metade e envia os e-mails da fila. Uma volta que falha não derruba o Worker; a próxima
/// tenta de novo.
/// </summary>
public sealed partial class Rotinas(IServiceScopeFactory escopos, ILogger<Rotinas> log) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(10);
    private const int EventosPorVolta = 50;
    private const int ContratosPorVolta = 2;
    private const int EmailsPorVolta = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var relogio = new PeriodicTimer(Intervalo);
        do
        {
            try
            {
                using var escopo = escopos.CreateScope();
                var eventos = await escopo.ServiceProvider.GetRequiredService<ProcessadorDeEventos>().ProcessarPendentes(EventosPorVolta, stoppingToken);
                var retomados = await escopo.ServiceProvider.GetRequiredService<Retomadas>().Executar(ContratosPorVolta, stoppingToken);
                var emails = await escopo.ServiceProvider.GetRequiredService<EnvioDeEmails>().Executar(EmailsPorVolta, stoppingToken);
                if (eventos + retomados + emails > 0)
                {
                    LogVolta(log, eventos, retomados, emails);
                }
            }
            catch (Exception erro) when (erro is not OperationCanceledException)
            {
                LogFalha(log, erro);
            }
        }
        while (await relogio.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Volta do Worker: {Eventos} eventos aplicados, {Retomados} retomados, {Emails} e-mails")]
    private static partial void LogVolta(ILogger log, int eventos, int retomados, int emails);

    [LoggerMessage(Level = LogLevel.Error, Message = "Volta do Worker falhou")]
    private static partial void LogFalha(ILogger log, Exception erro);
}
