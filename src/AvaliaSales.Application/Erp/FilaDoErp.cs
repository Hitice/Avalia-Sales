using AvaliaSales.Domain.Erp;
using AvaliaSales.Domain.Pedidos;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Erp;

public sealed record EnvioDto(Guid PedidoId, string Cliente, int Quantidade, long ValorCents, SituacaoDoEnvio Situacao, string? NumeroNoErp, int Tentativas, string? UltimoErro, DateTimeOffset CriadoEm);

public sealed record IntegracaoDoErp(bool Ligada);

/// <summary>
/// Põe o pedido pago na fila do ERP, na mesma transação do pagamento, e mostra a fila à operadora. Sem o endereço do
/// Protheus a integração está desligada e nada entra na fila: não há para onde o envio ir (ADR 0014).
/// </summary>
public sealed class FilaDoErp(IBancoDeDados banco, IErp erp, TimeProvider relogio)
{
    public IntegracaoDoErp Integracao() => new(erp.Configurado);

    /// <summary>O revendedor vem à parte: pedido recém-criado só ganha o dele no SaveChanges.</summary>
    public async Task Enfileirar(PedidoDePlacas pedido, Guid revendedorId, CancellationToken ct)
    {
        if (!erp.Configurado)
        {
            return;
        }

        if (await banco.EnviosAoErp.AnyAsync(e => e.PedidoId == pedido.Id, ct) || banco.EnviosAoErp.Local.Any(e => e.PedidoId == pedido.Id))
        {
            return;
        }

        var revendedor = await banco.Revendedores.AsNoTracking().Where(r => r.Id == revendedorId).Select(r => new { r.Nome, r.Cnpj }).SingleAsync(ct);
        banco.EnviosAoErp.Add(new EnvioAoErp(pedido.Id, revendedorId, revendedor.Nome, revendedor.Cnpj, pedido.Quantidade, pedido.ValorCents, relogio.GetUtcNow()));
    }

    /// <summary>A fila do ERP, do mais novo ao mais antigo; a busca vale para o cliente, o número no ERP e a situação.</summary>
    public async Task<Pagina<EnvioDto>> Listar(string? busca, PedidoDePagina pedido, CancellationToken ct)
    {
        var consulta = banco.EnviosAoErp.AsNoTracking();
        var termo = Paginacao.Termo(busca);
        if (termo.Length > 0)
        {
            var situacoes = Enum.GetValues<SituacaoDoEnvio>().Where(s => s.ToString().ToLowerInvariant().Contains(termo, StringComparison.Ordinal)).ToList();
            consulta = consulta.Where(e => Busca.Normalizar(e.Cliente).Contains(Busca.Normalizar(termo)) || (e.NumeroNoErp != null && Busca.Normalizar(e.NumeroNoErp).Contains(Busca.Normalizar(termo))) || situacoes.Contains(e.Situacao));
        }

        return await consulta.OrderByDescending(e => e.CriadoEm).ThenByDescending(e => e.PedidoId)
            .Select(e => new EnvioDto(e.PedidoId, e.Cliente, e.Quantidade, e.ValorCents, e.Situacao, e.NumeroNoErp, e.Tentativas, e.UltimoErro, e.CriadoEm))
            .PaginarAsync(pedido, ct);
    }

    public async Task Reenviar(Guid pedidoId, CancellationToken ct)
    {
        var envio = await banco.EnviosAoErp.SingleOrDefaultAsync(e => e.PedidoId == pedidoId, ct)
            ?? throw new Domain.RegraDeNegocioException("Pedido fora da fila do ERP.");
        envio.Reenviar();
        await banco.SaveChangesAsync(ct);
    }
}
