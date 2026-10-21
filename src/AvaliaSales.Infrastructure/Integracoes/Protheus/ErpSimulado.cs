using AvaliaSales.Application.Erp;

namespace AvaliaSales.Infrastructure.Integracoes.Protheus;

/// <summary>
/// Protheus de mentira para o ambiente local: devolve um número de pedido de
/// venda tirado da chave, o mesmo toda vez, como o ERP faria com a chave repetida.
/// </summary>
public sealed class ErpSimulado : IErp
{
    public bool Configurado => true;

    public Task<string> EnviarPedidoDeVenda(PedidoDeVendaNoErp pedido, CancellationToken ct) =>
        Task.FromResult("SIM" + pedido.Chave.Replace("-", string.Empty, StringComparison.Ordinal)[^6..].ToUpperInvariant());
}
