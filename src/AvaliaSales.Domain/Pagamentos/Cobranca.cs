namespace AvaliaSales.Domain.Pagamentos;

public enum OrigemDaCobranca
{
    Venda = 1,
    PedidoDePlacas = 2,
    Renovacao = 3,
}

public enum MeioDePagamento
{
    Pix = 1,
    Cartao = 2,

    /// <summary>Checkout do Pagar.me: quem paga escolhe Pix, cartão ou boleto na página dele.</summary>
    Link = 3,
}

public enum SituacaoDaCobranca
{
    Pendente = 1,
    Paga = 2,
    Cancelada = 3,
    Falhou = 4,
    Estornada = 5,
}

/// <summary>
/// Uma cobrança ao cliente final no Pagar.me. Nasce pendente e gravada antes da
/// chamada ao Pagar.me, com o próprio id como código do pedido: se a chamada
/// cair no meio, o pedido é achado depois pelo código, sem cobrar de novo.
/// </summary>
public sealed class Cobranca : IDoRevendedor
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid RevendedorId { get; private set; }
    public OrigemDaCobranca Origem { get; private set; }
    public Guid? OrigemId { get; private set; }
    public MeioDePagamento Meio { get; private set; }
    public long ValorCents { get; private set; }
    public long TaxaAvaliaCents { get; private set; }
    public SituacaoDaCobranca Situacao { get; private set; } = SituacaoDaCobranca.Pendente;
    public string? PedidoNoGateway { get; private set; }

    /// <summary>Link de pagamento (pl_...) e o endereço dele, quando a cobrança é pelo checkout do Pagar.me.</summary>
    public string? LinkNoGateway { get; private set; }

    public string? UrlDoLink { get; private set; }
    public string? PixCopiaECola { get; private set; }
    public string? PixQrCodeUrl { get; private set; }
    public DateTimeOffset? PixExpiraEm { get; private set; }
    public DateTimeOffset? PagaEm { get; private set; }
    public DateTimeOffset CriadaEm { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>Última reconsulta ao Pagar.me, para a rotina que cobre webhook perdido não repetir à toa.</summary>
    public DateTimeOffset? ConferidaEm { get; private set; }

    private Cobranca()
    {
    }

    public Cobranca(OrigemDaCobranca origem, Guid? origemId, MeioDePagamento meio, long valorCents, int taxaAvaliaBps)
    {
        Origem = origem;
        OrigemId = origemId;
        Meio = meio;
        ValorCents = valorCents;
        TaxaAvaliaCents = DivisaoDaCobranca.Calcular(valorCents, taxaAvaliaBps).AvaliaCents;
    }

    /// <summary>Código do pedido no Pagar.me: o id sem hífens, 32 dos 52 caracteres aceitos.</summary>
    public string Codigo => Id.ToString("N");

    public Divisao Divisao => new(TaxaAvaliaCents, ValorCents - TaxaAvaliaCents);

    public void RegistrarPedido(string pedido, string? pixCopiaECola, string? pixQrCodeUrl, DateTimeOffset? pixExpiraEm)
    {
        if (PedidoNoGateway is not null && PedidoNoGateway != pedido)
        {
            throw new InvalidOperationException($"A cobrança {Id} já está ligada ao pedido {PedidoNoGateway}.");
        }

        PedidoNoGateway = pedido;
        PixCopiaECola = pixCopiaECola;
        PixQrCodeUrl = pixQrCodeUrl;
        PixExpiraEm = pixExpiraEm;
    }

    public void MarcarConferida(DateTimeOffset agora) => ConferidaEm = agora;

    public void RegistrarLink(string link, string url)
    {
        LinkNoGateway = link;
        UrlDoLink = url;
    }

    /// <summary>
    /// Aplica a situação lida no Pagar.me. Só anda para a frente: um evento
    /// atrasado de pendente não desfaz um pagamento. Devolve se mudou.
    /// </summary>
    public bool Atualizar(SituacaoDaCobranca lida, DateTimeOffset? pagaEm)
    {
        var permitido = (Situacao, lida) switch
        {
            (SituacaoDaCobranca.Pendente, SituacaoDaCobranca.Paga or SituacaoDaCobranca.Cancelada or SituacaoDaCobranca.Falhou) => true,
            (SituacaoDaCobranca.Paga, SituacaoDaCobranca.Estornada) => true,
            _ => false,
        };

        if (!permitido)
        {
            return false;
        }

        Situacao = lida;
        if (lida == SituacaoDaCobranca.Paga)
        {
            PagaEm = pagaEm ?? DateTimeOffset.UtcNow;
        }

        return true;
    }
}
