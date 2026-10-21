using AvaliaSales.Domain.Erp;
using AvaliaSales.Domain.Pedidos;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Erp;

public sealed record EnvioDto(Guid PedidoId, string Cliente, int Quantidade, long ValorCents, SituacaoDoEnvio Situacao, string? NumeroNoErp, int Tentativas, string? UltimoErro, DateTimeOffset CriadoEm);

/// <summary>Põe o pedido pago na fila do ERP, na mesma transação do pagamento, e mostra a fila à operadora.</summary>
public sealed class FilaDoErp(IBancoDeDados banco, TimeProvider relogio)
{
    /// <summary>O revendedor vem à parte: pedido recém-criado só ganha o dele no SaveChanges.</summary>
    public async Task Enfileirar(PedidoDePlacas pedido, Guid revendedorId, CancellationToken ct)
    {
        if (await banco.EnviosAoErp.AnyAsync(e => e.PedidoId == pedido.Id, ct) || banco.EnviosAoErp.Local.Any(e => e.PedidoId == pedido.Id))
        {
            return;
        }

        var revendedor = await banco.Revendedores.AsNoTracking().Where(r => r.Id == revendedorId).Select(r => new { r.Nome, r.Cnpj }).SingleAsync(ct);
        banco.EnviosAoErp.Add(new EnvioAoErp(pedido.Id, revendedorId, revendedor.Nome, revendedor.Cnpj, pedido.Quantidade, pedido.ValorCents, relogio.GetUtcNow()));
    }

    public async Task<IReadOnlyList<EnvioDto>> Listar(CancellationToken ct) =>
        await banco.EnviosAoErp.AsNoTracking().OrderByDescending(e => e.CriadoEm).Take(200)
            .Select(e => new EnvioDto(e.PedidoId, e.Cliente, e.Quantidade, e.ValorCents, e.Situacao, e.NumeroNoErp, e.Tentativas, e.UltimoErro, e.CriadoEm))
            .ToListAsync(ct);

    public async Task Reenviar(Guid pedidoId, CancellationToken ct)
    {
        var envio = await banco.EnviosAoErp.SingleOrDefaultAsync(e => e.PedidoId == pedidoId, ct)
            ?? throw new Domain.RegraDeNegocioException("Este pedido não está na fila do ERP.");
        envio.Reenviar();
        await banco.SaveChangesAsync(ct);
    }
}
