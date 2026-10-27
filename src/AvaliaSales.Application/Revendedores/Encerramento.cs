using AvaliaSales.Application.Contratos;
using AvaliaSales.Application.Equipe;
using AvaliaSales.Domain;
using AvaliaSales.Domain.Acesso;
using AvaliaSales.Domain.Auditoria;
using AvaliaSales.Domain.Contratos;
using AvaliaSales.Domain.Placas;
using AvaliaSales.Domain.Revendedores;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Revendedores;

/// <summary>
/// Passa as placas no ar, os clientes delas e os links curtos para outra revenda,
/// na transação aberta. É a única escrita que muda o revendedor de uma linha.
/// </summary>
public interface ITransferenciaDeClientes
{
    Task<int> Transferir(Guid origem, Guid destino, DateTimeOffset quando, CancellationToken ct);
}

public sealed record EstadoDoEncerramento(DateTimeOffset? PedidoEm, DateTimeOffset? EncerradaEm, DateTimeOffset? ExportacaoAte, MeuContrato? Distrato,
    DateTimeOffset? ReaberturaPedidaEm);

public sealed record PedidoDeReabertura(string? Motivo);

/// <summary>
/// O dono encerra a conta pela tela. O Worker gera o distrato com os dados da
/// plataforma e manda à D4Sign, como o contrato; a assinatura encerra a conta
/// (<see cref="EfeitosDoEncerramento"/>). Revenda que nunca assinou o contrato não
/// tem o que distratar e encerra na hora.
/// </summary>
public sealed class Encerramento(IBancoDeDados banco, IRevendedorAtual atual, ContratoDaRevenda contratos, IAssinaturaEletronica assinatura,
    EfeitosDoEncerramento efeitos, Notificacoes.Notificacoes notificacoes, Auditoria.Auditoria auditoria, Atendimento.Chamados chamados, TimeProvider relogio)
{
    public async Task<EstadoDoEncerramento> Ler(CancellationToken ct)
    {
        var revendedor = await Revendedor(ct);
        return new EstadoDoEncerramento(revendedor.EncerramentoPedidoEm, revendedor.EncerradaEm, revendedor.ExportacaoAte,
            await contratos.Ler(TipoDeContrato.Distrato, ct), revendedor.ReaberturaPedidaEm);
    }

    public async Task<EstadoDoEncerramento> Pedir(CancellationToken ct)
    {
        var revendedor = await Revendedor(ct);
        var agora = relogio.GetUtcNow();
        revendedor.PedirEncerramento(agora);
        // Sem receptora, o distrato não teria para onde levar os clientes: recusa no pedido, antes de gerar documento.
        if (await efeitos.Receptora(revendedor.Id, ct) is null)
        {
            throw new RegraDeNegocioException("Encerramento indisponível no momento. Abra um chamado no Suporte.");
        }

        auditoria.Registrar(AcaoAuditada.EncerramentoPedido, "Conta", revendedor.Id.ToString());
        if (revendedor.ContratoAssinadoEm is null)
        {
            await using var transacao = await banco.Database.BeginTransactionAsync(ct);
            await efeitos.Aplicar(revendedor, agora, ct);
            await banco.SaveChangesAsync(ct);
            await transacao.CommitAsync(ct);
        }
        else
        {
            notificacoes.ParaOperadora(revendedor.Id, "Encerramento pedido", $"{revendedor.Nome} pediu o encerramento da conta. O distrato vai para assinatura.", "/operadora/revendedores");
            await banco.SaveChangesAsync(ct);
        }

        return await Ler(ct);
    }

    /// <summary>Até o distrato ser assinado, o dono desiste: o documento é cancelado na D4Sign.</summary>
    public async Task<EstadoDoEncerramento> Desistir(CancellationToken ct)
    {
        var revendedor = await Revendedor(ct);
        if (revendedor.EncerramentoPedidoEm is null)
        {
            throw new RegraDeNegocioException("Sem pedido de encerramento.");
        }

        var distrato = await banco.Contratos
            .Where(c => c.Tipo == TipoDeContrato.Distrato && (c.Situacao == SituacaoDoContrato.Rascunho || c.Situacao == SituacaoDoContrato.AguardandoAssinatura))
            .OrderByDescending(c => c.CriadoEm).FirstOrDefaultAsync(ct);
        if (distrato is not null)
        {
            if (distrato.DocumentoNaAssinatura is { } documento)
            {
                await assinatura.Cancelar(documento, "O revendedor desistiu do encerramento.", ct);
            }

            distrato.Atualizar(SituacaoDoContrato.Cancelado, relogio.GetUtcNow());
        }

        revendedor.DesistirDoEncerramento();
        auditoria.Registrar(AcaoAuditada.EncerramentoDesistido, "Conta", revendedor.Id.ToString());
        notificacoes.ParaOperadora(revendedor.Id, "Encerramento desistido", $"{revendedor.Nome} desistiu de encerrar a conta.", "/operadora/revendedores");
        await banco.SaveChangesAsync(ct);
        return await Ler(ct);
    }

    /// <summary>
    /// A conta encerrada pede para voltar pelo suporte: abre um chamado, que a
    /// equipe da Avalia responde, e a operadora aprova ou recusa em Revendedores.
    /// </summary>
    public async Task<EstadoDoEncerramento> PedirReabertura(PedidoDeReabertura pedido, CancellationToken ct)
    {
        var revendedor = await Revendedor(ct);
        revendedor.PedirReabertura(relogio.GetUtcNow());
        auditoria.Registrar(AcaoAuditada.ReaberturaPedida, "Conta", revendedor.Id.ToString(), null, pedido.Motivo);
        notificacoes.ParaOperadora(revendedor.Id, "Reabertura pedida", $"{revendedor.Nome} pediu para reabrir a conta encerrada.", "/operadora/revendedores");
        var motivo = string.IsNullOrWhiteSpace(pedido.Motivo) ? string.Empty : $"\n\nMotivo: {pedido.Motivo.Trim()}";
        await chamados.Abrir("Reabertura da conta", $"Peço a reabertura da conta encerrada pelo distrato.{motivo}", ct);
        return await Ler(ct);
    }

    private Task<Revendedor> Revendedor(CancellationToken ct) =>
        banco.Revendedores.SingleAsync(r => r.Id == atual.RevendedorId, ct);
}

/// <summary>
/// O que o distrato assinado faz, na transação de quem chama: data efetiva,
/// baixa do estoque (comprada é da revenda, sem devolução), equipe desligada e
/// os clientes ativos, com as placas no ar e os links curtos deles, para a
/// revenda receptora (ADR 0029). O dono continua entrando para consultar e exportar até
/// <see cref="Revendedor.ExportacaoAte"/>.
/// </summary>
public sealed class EfeitosDoEncerramento(IBancoDeDados banco, IContas contas, ITransferenciaDeClientes transferencia, Notificacoes.Notificacoes notificacoes)
{
    public const string SemReceptora = "Nenhuma revenda receptora. Marque uma em Revendedores.";

    /// <summary>A revenda marcada pela operadora para receber os clientes, se ativa (nem encerrada nem suspensa) e diferente da que encerra.</summary>
    public Task<Guid?> Receptora(Guid origem, CancellationToken ct) =>
        banco.Revendedores.AsNoTracking().Where(r => r.ReceptoraNoEncerramento && r.Id != origem && r.EncerradaEm == null && r.SuspensaEm == null)
            .Select(r => (Guid?)r.Id).SingleOrDefaultAsync(ct);

    public async Task Aplicar(Revendedor revendedor, DateTimeOffset quando, CancellationToken ct)
    {
        if (revendedor.EncerradaEm is not null)
        {
            return;
        }

        // Antes de qualquer efeito: sem receptora, a transação volta e o distrato assinado é aplicado de novo na próxima conferência.
        var destino = await Receptora(revendedor.Id, ct) ?? throw new RegraDeNegocioException(SemReceptora);
        revendedor.Encerrar(quando);
        var baixadas = await banco.Placas.Where(p => p.RevendedorId == revendedor.Id && p.Situacao == SituacaoDaPlaca.EmEstoque)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Situacao, SituacaoDaPlaca.Baixada).SetProperty(p => p.ConsignadaA, (Guid?)null)
                .SetProperty(p => p.MotivoDaBaixa, MotivoDaBaixa.Encerramento).SetProperty(p => p.BaixadaEm, (DateTimeOffset?)quando), ct);
        await banco.Vendedores.Where(v => v.RevendedorId == revendedor.Id).ExecuteUpdateAsync(s => s.SetProperty(v => v.Ativo, false), ct);
        await contas.DesativarLogins(revendedor.Id, Papel.Vendedor, ct);

        var transferidas = await transferencia.Transferir(revendedor.Id, destino, quando, ct);
        var clientes = $"{transferidas} {(transferidas == 1 ? "placa no ar passou" : "placas no ar passaram")} para a revenda receptora.";
        notificacoes.ParaOperadora(revendedor.Id, "Conta encerrada",
            $"{revendedor.Nome} encerrou a conta. {clientes} {baixadas} {(baixadas == 1 ? "placa em estoque teve" : "placas em estoque tiveram")} baixa.",
            "/operadora/revendedores");
    }
}
