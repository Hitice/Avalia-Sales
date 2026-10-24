using AvaliaSales.Infrastructure.Integracoes;

namespace AvaliaSales.Worker;

/// <summary>
/// A cada volta: processa a caixa de entrada dos webhooks, retoma o que
/// ficou pela metade, envia os e-mails da fila e leva os pedidos pagos ao ERP.
/// Cada rotina roda no próprio escopo e no próprio try: uma que falha (o ERP
/// fora do ar, um contrato com erro) não impede as outras nesta volta, e a
/// próxima volta tenta de novo.
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
            var eventos = await Rodar("eventos", (sp, ct) => sp.GetRequiredService<ProcessadorDeEventos>().ProcessarPendentes(EventosPorVolta, ct), stoppingToken);
            var retomados = await Rodar("retomadas", (sp, ct) => sp.GetRequiredService<Retomadas>().Executar(ContratosPorVolta, ct), stoppingToken);
            var emails = await Rodar("e-mails", (sp, ct) => sp.GetRequiredService<EnvioDeEmails>().Executar(EmailsPorVolta, ct), stoppingToken);
            var erp = await Rodar("ERP", (sp, ct) => sp.GetRequiredService<EnvioAoErpRotina>().Executar(EmailsPorVolta, ct), stoppingToken);
            if (eventos + retomados + emails + erp > 0)
            {
                LogVolta(log, eventos, retomados, emails, erp);
            }
        }
        while (await relogio.WaitForNextTickAsync(stoppingToken));
    }

    private async Task<int> Rodar(string rotina, Func<IServiceProvider, CancellationToken, Task<int>> fazer, CancellationToken ct)
    {
        try
        {
            using var escopo = escopos.CreateScope();
            return await fazer(escopo.ServiceProvider, ct);
        }
        catch (Exception erro) when (erro is not OperationCanceledException)
        {
            LogFalha(log, rotina, erro);
            return 0;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Volta do Worker: {Eventos} eventos aplicados, {Retomados} retomados, {Emails} e-mails, {Erp} pedidos no ERP")]
    private static partial void LogVolta(ILogger log, int eventos, int retomados, int emails, int erp);

    [LoggerMessage(Level = LogLevel.Error, Message = "Rotina {Rotina} do Worker falhou")]
    private static partial void LogFalha(ILogger log, string rotina, Exception erro);
}
