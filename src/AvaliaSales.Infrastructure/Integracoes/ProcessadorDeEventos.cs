using System.Text.Json;
using AvaliaSales.Application;
using AvaliaSales.Application.Avisos;
using AvaliaSales.Application.Contratos;
using AvaliaSales.Application.Revendedores;
using AvaliaSales.Application.Pagamentos;
using AvaliaSales.Domain.Contratos;
using AvaliaSales.Domain.Integracoes;
using AvaliaSales.Infrastructure.Banco;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    FilaDeEmails emails,
    IOptions<OpcoesDaPlataforma> plataforma,
    TimeProvider relogio,
    ILogger<ProcessadorDeEventos> log)
{
    /// <summary>Eventos do Pagar.me que dizem que alguém pagou: sem vínculo, o dinheiro entrou e nada na plataforma andou.</summary>
    private static readonly string[] EventosDePagamento = ["order.paid", "charge.paid"];

    public async Task<int> ProcessarPendentes(int lote, CancellationToken ct)
    {
        var pendentes = await banco.EventosExternos.AsNoTracking()
            .Where(e => e.ProcessadoEm == null && e.Tentativas < EventoExterno.TentativasMaximas)
            .OrderBy(e => e.RecebidoEm)
            .Take(lote)
            .Select(e => new { e.Id, e.Origem, e.Chave, e.Tipo, e.IdExterno })
            .ToListAsync(ct);

        var aplicados = 0;
        foreach (var evento in pendentes)
        {
            try
            {
                if (await Processar(evento.Id, evento.Origem, evento.Chave, evento.Tipo, evento.IdExterno, ct))
                {
                    aplicados++;
                }
            }
            catch (Exception erro) when (erro is not OperationCanceledException)
            {
                LogFalha(log, evento.Id, erro);
                await banco.EventosExternos.Where(e => e.Id == evento.Id).ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.Tentativas, e => e.Tentativas + 1)
                    .SetProperty(e => e.UltimoErro, erro.Message.Length > 500 ? erro.Message.Substring(0, 500) : erro.Message), ct);
            }
        }

        return aplicados;
    }

    private async Task<bool> Processar(Guid id, string origem, string chave, string tipo, string idExterno, CancellationToken ct)
    {
        var vinculo = await banco.Vinculos.AsNoTracking()
            .SingleOrDefaultAsync(v => v.Origem == origem && v.IdExterno == chave, ct);
        if (vinculo is null && origem == VinculoExterno.Pagarme && EventosDePagamento.Contains(tipo))
        {
            await AvisarPagamentoSemVinculo(id, chave, tipo, idExterno, ct);
            return false;
        }

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
            mudou = cobranca is not null && await escopo.ServiceProvider.GetRequiredService<Cobrancas>().Aplicar(cobranca, pedido, ct);
        }
        else if (situacao is not null)
        {
            var doDocumento = await doRevendedor.Contratos.Where(c => c.DocumentoNaAssinatura == chave).Select(c => (Guid?)c.Id).SingleOrDefaultAsync(ct);
            if (doDocumento is { } contratoId)
            {
                await ConferenciaDeAssinatura.Travar(doRevendedor, contratoId, ct);
                var contrato = await doRevendedor.Contratos.SingleAsync(c => c.Id == contratoId, ct);
                mudou = await escopo.ServiceProvider.GetRequiredService<EfeitosDaAssinatura>().Aplicar(contrato, situacao.Value, relogio.GetUtcNow(), ct);
            }
        }

        await doRevendedor.SaveChangesAsync(ct);
        await Encerrar(doRevendedor, id, null, ct);
        await transacao.CommitAsync(ct);
        return mudou;
    }

    /// <summary>
    /// Pagamento que não acha a cobrança: o pedido gerado pelo link pode não repetir o order_code, e aí a revenda pagou e o pedido
    /// fica parado. Sem vínculo não há revenda, então o aviso vai ao e-mail da operadora (a notificação do sino é sempre de uma
    /// revenda), com o pedido no Pagar.me para ela achar o pagamento. O evento fecha como antes; a chave do e-mail é o id do
    /// evento, e o e-mail e o fechamento vão na mesma transação, então reprocessar não avisa duas vezes.
    /// </summary>
    private async Task AvisarPagamentoSemVinculo(Guid id, string chave, string tipo, string idExterno, CancellationToken ct)
    {
        var corpo = await banco.EventosExternos.AsNoTracking().Where(e => e.Id == id).Select(e => e.Corpo).SingleAsync(ct);
        var pedido = PedidoDoEvento(corpo) ?? "não informado";
        LogPagamentoSemVinculo(log, idExterno, pedido);
        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        if (plataforma.Value.EmailDaOperadora is { Length: > 0 } operadora)
        {
            emails.Enfileirar($"pagarme-sem-vinculo:{idExterno}", operadora, $"Pagamento sem cobrança na plataforma - Pagar.me {pedido}",
                $"""
                O Pagar.me avisou um pagamento ({tipo}) que não corresponde a nenhuma cobrança da plataforma.

                Pedido no Pagar.me: {pedido}
                Código informado: {(chave.Length > 0 ? chave : "nenhum")}
                Evento: {idExterno}

                Confira no painel do Pagar.me de qual revenda é o pagamento. Se for um pedido de placas, registre o pagamento na tela do pedido.
                """);
            await banco.SaveChangesAsync(ct);
        }

        await Encerrar(banco, id, "sem vínculo: pagamento avisado à operadora", ct);
        await transacao.CommitAsync(ct);
    }

    /// <summary>O id do pedido no Pagar.me: em order.paid é o próprio dado; em charge.paid, o pedido da cobrança.</summary>
    private static string? PedidoDoEvento(string corpo)
    {
        try
        {
            using var documento = JsonDocument.Parse(corpo);
            if (!documento.RootElement.TryGetProperty("data", out var dados) || dados.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var pedido = dados.TryGetProperty("order", out var doPedido) && doPedido.ValueKind == JsonValueKind.Object ? doPedido : dados;
            return pedido.TryGetProperty("id", out var idDoPedido) && idDoPedido.ValueKind == JsonValueKind.String ? idDoPedido.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private Task<int> Encerrar(BancoDeDados alvo, Guid id, string? nota, CancellationToken ct) =>
        alvo.EventosExternos.Where(e => e.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(e => e.ProcessadoEm, relogio.GetUtcNow())
            .SetProperty(e => e.UltimoErro, nota), ct);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Evento externo {Id} falhou e volta para a fila")]
    private static partial void LogFalha(ILogger log, Guid id, Exception erro);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pagamento do Pagar.me sem cobrança na plataforma: evento {Evento}, pedido {Pedido}")]
    private static partial void LogPagamentoSemVinculo(ILogger log, string evento, string pedido);
}
