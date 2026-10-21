namespace AvaliaSales.Domain.Pedidos;

public enum SituacaoDoPedido
{
    AguardandoPagamento = 1,
    Pago = 2,
    Despachado = 3,
    Ativado = 4,
    Cancelado = 5,
}

/// <summary>
/// Pedido de placas do revendedor à Avalia. Pago antes do envio; despachado
/// com um lote da mesma quantidade; ativado quando o revendedor digita o
/// número do lote. Só anda para a frente.
/// </summary>
public sealed class PedidoDePlacas : IAtendidoPelaOperadora
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid RevendedorId { get; private set; }
    public int Quantidade { get; private set; }
    public long ValorCents { get; private set; }
    public SituacaoDoPedido Situacao { get; private set; } = SituacaoDoPedido.AguardandoPagamento;
    public DateTimeOffset CriadoEm { get; private set; }
    public DateTimeOffset? PagoEm { get; private set; }
    public Guid? LoteId { get; private set; }
    public DateTimeOffset? DespachadoEm { get; private set; }
    public DateTimeOffset? AtivadoEm { get; private set; }

    private PedidoDePlacas()
    {
    }

    public PedidoDePlacas(Pacote pacote, DateTimeOffset agora)
    {
        if (!pacote.Ativo)
        {
            throw new RegraDeNegocioException("Este pacote não está à venda.");
        }

        Quantidade = pacote.Quantidade;
        ValorCents = pacote.PrecoCents;
        CriadoEm = agora;
    }

    public long CustoPorPlacaCents => ValorCents / Quantidade;

    public void RegistrarPagamento(DateTimeOffset agora)
    {
        Exigir(SituacaoDoPedido.AguardandoPagamento, "Só pedido aguardando pagamento pode ser pago.");
        Situacao = SituacaoDoPedido.Pago;
        PagoEm = agora;
    }

    public void Despachar(Guid loteId, DateTimeOffset agora)
    {
        Exigir(SituacaoDoPedido.Pago, "Só pedido pago é despachado.");
        Situacao = SituacaoDoPedido.Despachado;
        LoteId = loteId;
        DespachadoEm = agora;
    }

    public void Ativar(DateTimeOffset agora)
    {
        Exigir(SituacaoDoPedido.Despachado, "Só pedido despachado é ativado.");
        Situacao = SituacaoDoPedido.Ativado;
        AtivadoEm = agora;
    }

    public void Cancelar()
    {
        Exigir(SituacaoDoPedido.AguardandoPagamento, "Pedido pago não é cancelado aqui: fale com a Avalia.");
        Situacao = SituacaoDoPedido.Cancelado;
    }

    private void Exigir(SituacaoDoPedido esperada, string recusa)
    {
        if (Situacao != esperada)
        {
            throw new RegraDeNegocioException(recusa);
        }
    }
}
