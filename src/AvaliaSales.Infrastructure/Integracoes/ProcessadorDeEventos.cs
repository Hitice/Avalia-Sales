using System.Text.Json;
using AvaliaSales.Application.Contratos;
using AvaliaSales.Application.Pagamentos;
using AvaliaSales.Domain.Contratos;
using AvaliaSales.Domain.Integracoes;
using AvaliaSales.Infrastructure.Banco;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AvaliaSales.Infrastructure.Integracoes;

/// <summary>
/// Processa a caixa de entrada, um evento por vez:
/// 1. descobre o revendedor pelo vínculo, sem revendedor na sessão;
/// 2. reconsulta a fonte (o corpo do webhook não é fonte de verdade), fora de transação;
/// 3. abre um escopo já isolado no revendedor, trava o evento com
///    "for update skip locked" e aplica o efeito e a marca de processado
///    na mesma transação.
/// </summary>
public sealed partial class ProcessadorDeEventos(
    IServiceScopeFactory escopos,
    BancoDeDados banco,
    IGatewayDePagamento gateway,
    IAssinaturaEletronica assinatura,
    TimeProvider relogio,
    ILogger<ProcessadorDeEventos> log)
{
    public async Task<int> ProcessarPendentes(int lote, CancellationToken ct)
    {
        var pendentes = await banco.EventosExternos.AsNoTracking()
            .Where(e => e.ProcessadoEm == null && e.Tentativas < EventoExterno.TentativasMaximas)
            .OrderBy(e => e.RecebidoEm)
            .Take(lote)
            .Select(e => new { e.Id, e.Origem, e.Chave })
            .ToListAsync(ct);

        var aplicados = 0;
        foreach (var evento in pendentes)
        {
            try
            {
                if (await Processar(evento.Id, evento.Origem, evento.Chave, ct))
                {
                    aplicados++;
                }
            }
            catch (Exception erro) when (erro is Application.IntegracaoException or DbUpdateException or InvalidOperationException or JsonException)
            {
                LogFalha(log, evento.Id, erro);
                await banco.EventosExternos.Where(e => e.Id == evento.Id).ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.Tentativas, e => e.Tentativas + 1)
                    .SetProperty(e => e.UltimoErro, erro.Message.Length > 500 ? erro.Message.Substring(0, 500) : erro.Message), ct);
            }
        }

        return aplicados;
    }

    private async Task<bool> Processar(Guid id, string origem, string chave, CancellationToken ct)
    {
        var vinculo = await banco.Vinculos.AsNoTracking()
            .SingleOrDefaultAsync(v => v.Origem == origem && v.IdExterno == chave, ct);
        if (vinculo is null)
        {
            await Encerrar(banco, id, "sem vínculo: evento de pedido ou documento que não é nosso", ct);
            return false;
        }

        // A fonte de verdade é a API, consultada antes de abrir a transação.
        PedidoDoGateway? pedido = null;
        SituacaoDoContrato? situacao = null;
        if (origem == VinculoExterno.Pagarme)
        {
            pedido = await gateway.BuscarPorCodigo(chave, ct);
        }
        else
        {
            situacao = await assinatura.LerSituacao(chave, ct);
        }

        using var escopo = escopos.CreateScope();
        escopo.ServiceProvider.GetRequiredService<RevendedorDaExecucao>().RevendedorId = vinculo.RevendedorId;
        var doRevendedor = escopo.ServiceProvider.GetRequiredService<BancoDeDados>();

        await using var transacao = await doRevendedor.Database.BeginTransactionAsync(ct);
        var travado = await doRevendedor.Database
            .SqlQuery<Guid>($"select id as \"Value\" from eventos_externos where id = {id} and processado_em is null for update skip locked")
            .ToListAsync(ct);
        if (travado.Count == 0)
        {
            return false;
        }

        var mudou = false;
        if (pedido is not null)
        {
            var cobranca = await doRevendedor.Cobrancas.SingleOrDefaultAsync(c => c.Id == Guid.ParseExact(chave, "N"), ct);
            mudou = cobranca is not null && Cobrancas.AplicarPedido(cobranca, pedido);
        }
        else if (situacao is not null)
        {
            var contrato = await doRevendedor.Contratos.SingleOrDefaultAsync(c => c.DocumentoNaAssinatura == chave, ct);
            mudou = contrato is not null && contrato.Atualizar(situacao.Value, relogio.GetUtcNow());
        }

        await doRevendedor.SaveChangesAsync(ct);
        await Encerrar(doRevendedor, id, null, ct);
        await transacao.CommitAsync(ct);
        return mudou;
    }

    private Task<int> Encerrar(BancoDeDados alvo, Guid id, string? nota, CancellationToken ct) =>
        alvo.EventosExternos.Where(e => e.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(e => e.ProcessadoEm, relogio.GetUtcNow())
            .SetProperty(e => e.UltimoErro, nota), ct);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Evento externo {Id} falhou e volta para a fila")]
    private static partial void LogFalha(ILogger log, Guid id, Exception erro);
}
