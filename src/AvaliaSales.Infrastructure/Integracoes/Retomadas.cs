using AvaliaSales.Application.Contratos;
using AvaliaSales.Application.Equipe;
using AvaliaSales.Domain.Acesso;
using AvaliaSales.Application;
using AvaliaSales.Domain;
using AvaliaSales.Application.Revendedores;
using AvaliaSales.Application.Pagamentos;
using AvaliaSales.Domain.Contratos;
using AvaliaSales.Domain.Pagamentos;
using AvaliaSales.Infrastructure.Banco;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AvaliaSales.Infrastructure.Integracoes;

/// <summary>
/// O que ficou pela metade quando uma chamada externa caiu: cobrança sem
/// pedido e contrato com envio incompleto. Também gera o contrato de revenda
/// de quem se cadastrou e ainda não tem, o distrato de quem pediu o
/// encerramento, desliga o dono quando acaba o prazo de exportação e cuida do
/// fim do teste gratuito (<see cref="Application.Vendas.TestesGratuitos"/>). Roda
/// revendedor por revendedor, porque o RLS só mostra as linhas do revendedor da sessão.
/// </summary>
public sealed partial class Retomadas(IServiceScopeFactory escopos, BancoDeDados banco, TimeProvider relogio, Microsoft.Extensions.Options.IOptions<Pagarme.OpcoesPagarme> pagarme,
    ILogger<Retomadas> log)
{
    /// <summary>Cobrança pendente mais velha que isso não é reconsultada: o Pix já expirou há muito.</summary>
    public static readonly TimeSpan JanelaDeConferencia = TimeSpan.FromDays(2);

    /// <summary>Reconsultas por revendedor a cada volta, para não gastar o limite do Pagar.me.</summary>
    public const int ConferenciasPorRodada = 20;

    /// <summary>Tempo para a criação normal terminar antes de a rotina mexer.</summary>
    public static readonly TimeSpan Carencia = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Contrato esperando assinatura há mais que isso, sem nova conferência, é
    /// reconsultado na D4Sign: o webhook pode se perder ou ser recusado.
    /// </summary>
    public static readonly TimeSpan IntervaloDeConferencia = TimeSpan.FromMinutes(30);

    /// <summary>Sem pedido no Pagar.me depois disso, a cobrança falhou.</summary>
    public static readonly TimeSpan PrazoDoPedido = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Cobrança paga é reconsultada por este tempo depois do pagamento: é a
    /// janela em que chega chargeback ou estorno feito no painel do Pagar.me,
    /// que o webhook pode não ter trazido.
    /// </summary>
    public static readonly TimeSpan JanelaDeContestacao = TimeSpan.FromDays(120);

    /// <summary>Cada cobrança paga é reconsultada no máximo uma vez neste intervalo.</summary>
    public static readonly TimeSpan IntervaloDasPagas = TimeSpan.FromHours(24);

    /// <summary>Cobranças pagas reconsultadas por revendedor a cada volta.</summary>
    public const int PagasPorRodada = 5;

    public async Task<int> Executar(int contratosPorRodada, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        var revendedores = await banco.Revendedores.AsNoTracking()
            .Select(r => new { r.Id, r.ContratoAssinadoEm, r.EncerramentoPedidoEm, r.EncerradaEm, r.ReabertaEm }).ToListAsync(ct);
        var retomados = 0;
        var contratosRestantes = contratosPorRodada;

        foreach (var (revendedorId, assinado, pedido, encerrada, reaberta) in revendedores.Select(r => (r.Id, r.ContratoAssinadoEm, r.EncerramentoPedidoEm, r.EncerradaEm, r.ReabertaEm)))
        {
            using var escopo = escopos.CreateScope();
            escopo.ServiceProvider.GetRequiredService<RevendedorDaExecucao>().RevendedorId = revendedorId;
            var doRevendedor = escopo.ServiceProvider.GetRequiredService<BancoDeDados>();

            var cobrancas = await doRevendedor.Cobrancas.AsNoTracking()
                .Where(c => c.Situacao == SituacaoDaCobranca.Pendente && c.PedidoNoGateway == null && c.LinkNoGateway == null && c.CriadaEm < agora - Carencia)
                .Select(c => c.Id)
                .ToListAsync(ct);
            // O teste gratuito que venceu sai do ar; o que está acabando gera o aviso.
            retomados += await escopo.ServiceProvider.GetRequiredService<Application.Vendas.TestesGratuitos>().Processar(ct);
            // A renovação anual é a receita que se repete: 30 e 7 dias antes do vencimento, a revenda e o cliente são avisados.
            retomados += await escopo.ServiceProvider.GetRequiredService<Application.Avisos.AvisosDeVencimento>().Processar(ct);
            // Lote despachado e nunca ativado segura a receita da operadora: lembra a revenda e, passado um mês, a própria operadora.
            retomados += await escopo.ServiceProvider.GetRequiredService<Application.Pedidos.PedidosDePlacas>().LembrarDaAtivacao(ct);

            var servicoDeCobranca = escopo.ServiceProvider.GetRequiredService<Cobrancas>();
            foreach (var id in cobrancas)
            {
                retomados += await Isolar(doRevendedor, id, () => servicoDeCobranca.Retomar(id, agora - PrazoDoPedido, ct), ct) ? 1 : 0;
            }

            var intervalo = TimeSpan.FromSeconds(pagarme.Value.ConferenciaEmSegundos);
            var limiteDoPedido = agora - JanelaDeConferencia;
            var limiteDoLink = agora - Cobrancas.ValidadeDoLink - JanelaDeConferencia;
            var pendentes = await doRevendedor.Cobrancas.AsNoTracking()
                // Link de pagamento também: o pedido só nasce quando o revendedor paga, e a reconsulta acha pelo código.
                .Where(c => c.Situacao == SituacaoDaCobranca.Pendente && (c.PedidoNoGateway != null || c.LinkNoGateway != null)
                    && (c.LinkNoGateway == null ? c.CriadaEm > limiteDoPedido : c.CriadaEm > limiteDoLink)
                    && (c.ConferidaEm == null ? c.CriadaEm < agora - intervalo : c.ConferidaEm < agora - intervalo))
                .OrderBy(c => c.ConferidaEm ?? c.CriadaEm)
                .Select(c => c.Id)
                .Take(ConferenciasPorRodada)
                .ToListAsync(ct);
            foreach (var id in pendentes)
            {
                retomados += await Isolar(doRevendedor, id, () => servicoDeCobranca.Conferir(id, agora, ct), ct) ? 1 : 0;
            }

            // Chargeback e estorno pelo painel: a paga volta ao Pagar.me uma vez por dia na janela de contestação.
            var limiteDaContestacao = agora - JanelaDeContestacao;
            var pagas = await doRevendedor.Cobrancas.AsNoTracking()
                .Where(c => c.Situacao == SituacaoDaCobranca.Paga && c.PedidoNoGateway != null && c.PagaEm > limiteDaContestacao
                    && (c.ConferidaEm == null ? c.PagaEm < agora - IntervaloDasPagas : c.ConferidaEm < agora - IntervaloDasPagas))
                .OrderBy(c => c.ConferidaEm ?? c.PagaEm)
                .Select(c => c.Id)
                .Take(PagasPorRodada)
                .ToListAsync(ct);
            foreach (var id in pagas)
            {
                retomados += await Isolar(doRevendedor, id, () => servicoDeCobranca.Conferir(id, agora, ct), ct) ? 1 : 0;
            }

            if (contratosRestantes <= 0)
            {
                continue;
            }

            if (encerrada is { } dataEfetiva)
            {
                // Passado o prazo de exportação do distrato (cláusula 3.3), o dono deixa de entrar.
                if (dataEfetiva.AddDays(Domain.Revendedores.Revendedor.DiasParaExportar) < agora)
                {
                    await escopo.ServiceProvider.GetRequiredService<IContas>().DesativarLogins(revendedorId, Papel.Dono, ct);
                }

                continue;
            }

            var documentos = escopo.ServiceProvider.GetRequiredService<DocumentosDaRevenda>();
            // Sem contrato assinado e sem um em andamento; depois de uma reabertura, só conta o contrato feito depois dela.
            if (assinado is null && pedido is null && !await doRevendedor.Contratos.AnyAsync(c => c.Tipo == TipoDeContrato.ContratoDoRevendedor
                    && (reaberta == null || c.CriadoEm > reaberta), ct))
            {
                await Gerar(() => documentos.GerarContrato(revendedorId, ct));
            }

            // Distrato gerado na mesma hora em que o dono desistiu fica sem uso: cancela antes de ir à D4Sign.
            if (pedido is null)
            {
                await doRevendedor.Contratos.Where(c => c.Tipo == TipoDeContrato.Distrato && c.Situacao == SituacaoDoContrato.Rascunho)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.Situacao, SituacaoDoContrato.Cancelado), ct);
            }

            if (pedido is not null && !await doRevendedor.Contratos.AnyAsync(c => c.Tipo == TipoDeContrato.Distrato
                    && (c.Situacao == SituacaoDoContrato.Rascunho || c.Situacao == SituacaoDoContrato.AguardandoAssinatura), ct))
            {
                await Gerar(() => documentos.GerarDistrato(revendedorId, ct));
            }

            // A conta padrão da D4Sign aceita 10 requisições por hora: poucos contratos por rodada.
            var contratos = await doRevendedor.Contratos.AsNoTracking()
                // Distrato de quem já desistiu não sai, mesmo que tenha sido gerado na mesma hora.
                .Where(c => c.Etapa < EtapaDoEnvio.Enviado && c.Situacao == SituacaoDoContrato.Rascunho && (c.Tipo != TipoDeContrato.Distrato || pedido != null)
                    && c.TentativasDeEnvio < Contratos.TentativasMaximas
                    && (c.CriadoEm < agora - Carencia || c.Etapa == EtapaDoEnvio.Criado && c.TentativasDeEnvio == 0))
                .OrderBy(c => c.CriadoEm)
                .Select(c => c.Id)
                .Take(contratosRestantes)
                .ToListAsync(ct);
            var servicoDeContrato = escopo.ServiceProvider.GetRequiredService<Contratos>();
            foreach (var id in contratos)
            {
                var resultado = await servicoDeContrato.Enviar(id, ct);
                retomados += resultado.Etapa == EtapaDoEnvio.Enviado ? 1 : 0;
                contratosRestantes--;
            }

            var limite = agora - IntervaloDeConferencia;
            var esperando = await doRevendedor.Contratos.AsNoTracking()
                .Where(c => c.Situacao == SituacaoDoContrato.AguardandoAssinatura && c.DocumentoNaAssinatura != null && (c.ConferidoEm ?? c.CriadoEm) < limite)
                .OrderBy(c => c.ConferidoEm ?? c.CriadoEm)
                .Select(c => c.Id)
                .Take(Math.Max(contratosRestantes, 0))
                .ToListAsync(ct);
            var conferencia = escopo.ServiceProvider.GetRequiredService<IConferenciaDeAssinatura>();
            foreach (var id in esperando)
            {
                try
                {
                    retomados += await conferencia.Conferir(id, pedidoDoDono: false, ct) ? 1 : 0;
                }
                catch (IntegracaoException erro)
                {
                    LogConferencia(log, id, erro.Message);
                }
                catch (RegraDeNegocioException erro)
                {
                    // Distrato assinado sem receptora marcada: este contrato espera a operadora e volta no
                    // próximo intervalo, sem parar a rodada das outras revendas.
                    LogConferencia(log, id, erro.Message);
                    doRevendedor.ChangeTracker.Clear();
                    await doRevendedor.Contratos.Where(c => c.Id == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.ConferidoEm, agora), ct);
                }

                contratosRestantes--;
            }
        }

        return retomados;
    }

    /// <summary>
    /// Uma cobrança que não aplica (pedido de outro id, regra recusada) não para a rodada
    /// das outras nem dos outros revendedores: o que ela deixou pela metade no contexto é
    /// descartado, e ela vai para o fim da fila de conferência. Pagar.me fora do ar
    /// (sem resposta ou 5xx) continua parando a rodada: as demais falhariam igual.
    /// </summary>
    private async Task<bool> Isolar(BancoDeDados doRevendedor, Guid cobrancaId, Func<Task<bool>> aplicar, CancellationToken ct)
    {
        try
        {
            return await aplicar();
        }
        catch (IntegracaoException erro) when (erro.Status is null or >= 500)
        {
            throw;
        }
        catch (Exception erro) when (erro is not OperationCanceledException)
        {
            doRevendedor.ChangeTracker.Clear();
            LogCobranca(log, cobrancaId, erro);
            await doRevendedor.Cobrancas.Where(c => c.Id == cobrancaId).ExecuteUpdateAsync(s => s.SetProperty(c => c.ConferidaEm, relogio.GetUtcNow()), ct);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Cobrança {Cobranca} não foi reconsultada e vai para o fim da fila")]
    private static partial void LogCobranca(ILogger log, Guid cobranca, Exception erro);

    /// <summary>O conversor de PDF fora do ar não para a rotina: a próxima volta gera de novo.</summary>
    private async Task Gerar(Func<Task<bool>> geracao)
    {
        try
        {
            await geracao();
        }
        catch (IntegracaoException erro)
        {
            LogGeracao(log, erro.Message);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reconsulta do contrato {Contrato} na D4Sign falhou e volta depois do intervalo: {Motivo}")]
    private static partial void LogConferencia(ILogger log, Guid contrato, string motivo);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Documento da revenda não foi gerado e volta na próxima rodada: {Motivo}")]
    private static partial void LogGeracao(ILogger log, string motivo);
}
