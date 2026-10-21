namespace AvaliaSales.Application.Erp;

/// <summary>
/// O pedido de venda que vai ao ERP. Contrato próprio da plataforma: do lado
/// do Protheus, um serviço REST em ADVPL recebe e grava o pedido de venda.
/// Chave é o id do pedido de placas, para o ERP recusar o repetido.
/// </summary>
public sealed record PedidoDeVendaNoErp(string Chave, string Cliente, string? Cnpj, string Produto, int Quantidade, long ValorUnitarioCents, long ValorTotalCents);

public interface IErp
{
    bool Configurado { get; }

    /// <summary>O número do pedido de venda no ERP. Enviar de novo a mesma chave devolve o mesmo número.</summary>
    Task<string> EnviarPedidoDeVenda(PedidoDeVendaNoErp pedido, CancellationToken ct);
}
