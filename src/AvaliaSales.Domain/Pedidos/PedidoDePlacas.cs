namespace AvaliaSales.Domain.Pedidos;

public enum SituacaoDoPedido
{
    AguardandoPagamento = 1,
    Pago = 2,
    Despachado = 3,
    Ativado = 4,
    Cancelado = 5,
    EmProducao = 6,
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
    public DateTimeOffset? ProducaoEm { get; private set; }
    public DateTimeOffset? DespachadoEm { get; private set; }

    /// <summary>A revenda busca a caixa na Avalia, em vez de receber pelo envio.</summary>
    public bool Retirada { get; private set; }

    public const int TransportadoraMaxima = 80;
    public const int RastreioMaximo = 40;

    /// <summary>Quem entrega o lote, quando não é retirada. O nome vem do cadastro de fornecedores e fica gravado no pedido.</summary>
    public string? Transportadora { get; private set; }

    /// <summary>O código para a revenda acompanhar a entrega.</summary>
    public string? CodigoDeRastreio { get; private set; }
    public DateTimeOffset? AtivadoEm { get; private set; }

    /// <summary>Despachado e nunca ativado: a receita da operadora só entra na ativação, então o pedido parado é cobrado da revenda.</summary>
    public const int DiasEntreLembretesDeAtivacao = 7;

    public const int LembretesDeAtivacaoMaximos = 3;
    public const int DiasParaAvisarAOperadora = 30;

    public int LembretesDeAtivacao { get; private set; }
    public DateTimeOffset? OperadoraAvisadaDaAtivacaoEm { get; private set; }

    public bool PrecisaLembrarDaAtivacao(DateTimeOffset agora) =>
        Situacao == SituacaoDoPedido.Despachado && DespachadoEm is { } quando && LembretesDeAtivacao < LembretesDeAtivacaoMaximos
        && agora >= quando.AddDays(DiasEntreLembretesDeAtivacao * (LembretesDeAtivacao + 1));

    public void LembrouDaAtivacao() => LembretesDeAtivacao++;

    public bool PrecisaAvisarAOperadoraDaAtivacao(DateTimeOffset agora) =>
        Situacao == SituacaoDoPedido.Despachado && OperadoraAvisadaDaAtivacaoEm is null && DespachadoEm is { } quando && agora >= quando.AddDays(DiasParaAvisarAOperadora);

    public void AvisouAOperadoraDaAtivacao(DateTimeOffset agora) => OperadoraAvisadaDaAtivacaoEm = agora;

    private PedidoDePlacas()
    {
    }

    public PedidoDePlacas(Pacote pacote, DateTimeOffset agora, Cupom? cupom = null)
    {
        if (!pacote.Ativo)
        {
            throw new RegraDeNegocioException("Pacote desativado.");
        }

        Quantidade = pacote.Quantidade;
        PrecoDoPacoteCents = pacote.PrecoCents;
        DescontoCents = cupom?.Aplicar(pacote.PrecoCents, agora) ?? 0;
        Cupom = cupom?.Codigo;
        ValorCents = PrecoDoPacoteCents - DescontoCents;
        CriadoEm = agora;
    }

    /// <summary>
    /// Pedido aberto pela operadora fora dos pacotes, com a quantidade e o valor combinados com a revenda: a placa
    /// avulsa com dois códigos, a reposição. Segue o mesmo caminho do pedido do pacote; valor zero nasce pago.
    /// </summary>
    public static PedidoDePlacas Avulso(Guid revendedorId, int quantidade, long valorCents, DateTimeOffset agora)
    {
        if (quantidade is < 1 or > Placas.Lote.QuantidadeMaxima)
        {
            throw new RegraDeNegocioException($"Pedido de 1 a {Placas.Lote.QuantidadeMaxima} placas.");
        }

        if (valorCents < 0)
        {
            throw new RegraDeNegocioException("Valor negativo. Use zero para o pedido sem custo.");
        }

        return new PedidoDePlacas
        {
            RevendedorId = revendedorId,
            Quantidade = quantidade,
            PrecoDoPacoteCents = valorCents,
            ValorCents = valorCents,
            CriadoEm = agora,
        };
    }

    /// <summary>O custo que a placa carrega é o que o revendedor pagou de fato, com o desconto.</summary>
    public long CustoPorPlacaCents => ValorCents / Quantidade;

    /// <summary>Cupom de 100%: nada a cobrar, o pedido já nasce pago.</summary>
    public bool Gratuito => ValorCents == 0;

    public void RegistrarPagamento(DateTimeOffset agora)
    {
        Exigir(SituacaoDoPedido.AguardandoPagamento, "Pedido não aguarda pagamento.");
        Situacao = SituacaoDoPedido.Pago;
        PagoEm = agora;
    }

    /// <summary>A Avalia começou a produzir as placas do pedido pago.</summary>
    public void IniciarProducao(DateTimeOffset agora)
    {
        Exigir(SituacaoDoPedido.Pago, "Só pedido pago entra em produção.");
        Situacao = SituacaoDoPedido.EmProducao;
        ProducaoEm = agora;
    }

    public void Despachar(Guid loteId, DateTimeOffset agora, bool retirada = false, string? transportadora = null, string? codigoDeRastreio = null)
    {
        if (Situacao is not (SituacaoDoPedido.Pago or SituacaoDoPedido.EmProducao))
        {
            throw new RegraDeNegocioException("Só pedido pago é despachado.");
        }

        transportadora = string.IsNullOrWhiteSpace(transportadora) ? null : transportadora.Trim();
        codigoDeRastreio = string.IsNullOrWhiteSpace(codigoDeRastreio) ? null : codigoDeRastreio.Trim();
        if (retirada && (transportadora is not null || codigoDeRastreio is not null))
        {
            throw new RegraDeNegocioException("Retirada na Avalia não tem transportadora nem rastreio.");
        }

        if (transportadora?.Length > TransportadoraMaxima || codigoDeRastreio?.Length > RastreioMaximo)
        {
            throw new RegraDeNegocioException($"Transportadora com até {TransportadoraMaxima} caracteres e rastreio com até {RastreioMaximo}.");
        }

        Situacao = SituacaoDoPedido.Despachado;
        LoteId = loteId;
        DespachadoEm = agora;
        Retirada = retirada;
        Transportadora = transportadora;
        CodigoDeRastreio = codigoDeRastreio;
    }

    public void Ativar(DateTimeOffset agora)
    {
        Exigir(SituacaoDoPedido.Despachado, "Só pedido despachado é ativado.");
        Situacao = SituacaoDoPedido.Ativado;
        AtivadoEm = agora;
    }

    /// <summary>
    /// A operadora desfaz o pedido pago que ainda não foi despachado: o dinheiro volta ao revendedor. Depois do despacho o lote
    /// está com ele, e a conversa é outra.
    /// </summary>
    public void EstornarPago()
    {
        if (Situacao is not (SituacaoDoPedido.Pago or SituacaoDoPedido.EmProducao))
        {
            throw new RegraDeNegocioException("Só pedido pago e não despachado se estorna. Após o despacho, combine com a revenda.");
        }

        Situacao = SituacaoDoPedido.Cancelado;
    }

    public void Cancelar()
    {
        Exigir(SituacaoDoPedido.AguardandoPagamento, "Pedido pago não se cancela. Fale com o suporte.");
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
