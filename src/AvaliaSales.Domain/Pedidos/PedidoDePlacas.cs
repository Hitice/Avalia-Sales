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
    /// <summary>O valor a pagar, já com o desconto do cupom.</summary>
    public long ValorCents { get; private set; }

    /// <summary>O preço do pacote no momento, antes do cupom.</summary>
    public long PrecoDoPacoteCents { get; private set; }

    public string? Cupom { get; private set; }
    public long DescontoCents { get; private set; }
    public SituacaoDoPedido Situacao { get; private set; } = SituacaoDoPedido.AguardandoPagamento;
    public DateTimeOffset CriadoEm { get; private set; }
    public DateTimeOffset? PagoEm { get; private set; }
    public Guid? LoteId { get; private set; }
    public DateTimeOffset? DespachadoEm { get; private set; }
    public DateTimeOffset? AtivadoEm { get; private set; }

    private PedidoDePlacas()
    {
    }

    public PedidoDePlacas(Pacote pacote, DateTimeOffset agora, Cupom? cupom = null)
    {
        if (!pacote.Ativo)
        {
            throw new RegraDeNegocioException("Este pacote não está à venda.");
        }

        Quantidade = pacote.Quantidade;
        PrecoDoPacoteCents = pacote.PrecoCents;
        DescontoCents = cupom?.Aplicar(pacote.PrecoCents, agora) ?? 0;
        Cupom = cupom?.Codigo;
        ValorCents = PrecoDoPacoteCents - DescontoCents;
        CriadoEm = agora;
    }

    /// <summary>O custo que a placa carrega é o que o revendedor pagou de fato, com o desconto.</summary>
    public long CustoPorPlacaCents => ValorCents / Quantidade;

    /// <summary>Cupom de 100%: nada a cobrar, o pedido já nasce pago.</summary>
    public bool Gratuito => ValorCents == 0;

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
