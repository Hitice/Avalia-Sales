namespace AvaliaSales.Domain.Pagamentos;

/// <summary>Quanto da cobrança vai para a Avalia e quanto para a revenda, em centavos.</summary>
public readonly record struct Divisao(long AvaliaCents, long RevendedorCents);

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

    /// <summary>Recebido fora do Pagar.me, no balcão: a cobrança só registra o que o vendedor declarou.</summary>
    Dinheiro = 4,
    OutroPix = 5,
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

    /// <summary>
    /// O que o split manda à Avalia: a parte dela nos serviços que executa, ou o
    /// pedido de placas inteiro. Venda de placa e renovação não mandam nada.
    /// </summary>
    public long ParteDaAvaliaCents { get; private set; }
    public SituacaoDaCobranca Situacao { get; private set; } = SituacaoDaCobranca.Pendente;

    /// <summary>
    /// Devolvido em parte: a venda do grupo desfeita sozinha, com o resto do
    /// grupo de pé. Devolvido tudo, a cobrança passa a estornada.
    /// </summary>
    public long EstornadoCents { get; private set; }
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

    /// <summary>A parte da Avalia nunca passa do valor cobrado; o resto é da revenda.</summary>
    public Cobranca(OrigemDaCobranca origem, Guid? origemId, MeioDePagamento meio, long valorCents, long parteDaAvaliaCents = 0)
    {
        if (valorCents <= 0)
        {
            throw new RegraDeNegocioException("Valor da cobrança precisa ser maior que zero.");
        }

        if (parteDaAvaliaCents < 0 || parteDaAvaliaCents > valorCents)
        {
            throw new RegraDeNegocioException("Valor abaixo da parte da Avalia no serviço.");
        }

        Origem = origem;
        OrigemId = origemId;
        Meio = meio;
        ValorCents = valorCents;
        ParteDaAvaliaCents = parteDaAvaliaCents;
    }

    /// <summary>
    /// Pagamento que não passou pelo Pagar.me: nasce paga, sem pedido no
    /// gateway. A parte da Avalia não foi retida no split e fica a acertar.
    /// </summary>
    public static Cobranca RecebidaPorFora(OrigemDaCobranca origem, Guid origemId, MeioDePagamento meio, long valorCents, DateTimeOffset agora,
        long parteDaAvaliaCents = 0)
    {
        if (meio is not (MeioDePagamento.Dinheiro or MeioDePagamento.OutroPix))
        {
            throw new RegraDeNegocioException("Escolha dinheiro ou outro Pix.");
        }

        return new Cobranca(origem, origemId, meio, valorCents, parteDaAvaliaCents)
        {
            Situacao = SituacaoDaCobranca.Paga,
            PagaEm = agora,
            CriadaEm = agora,
        };
    }

    public bool PorFora => Meio is MeioDePagamento.Dinheiro or MeioDePagamento.OutroPix;

    /// <summary>
    /// Confirmação por fora feita por engano. O Pix que caiu no Pagar.me não
    /// sai por aqui: esse só sai por estorno no Pagar.me.
    /// </summary>
    public void DesfazerRecebimento()
    {
        if (!PorFora || Situacao != SituacaoDaCobranca.Paga)
        {
            throw new RegraDeNegocioException("Só pagamento por fora confirmado se desfaz.");
        }

        Situacao = SituacaoDaCobranca.Estornada;
    }

    /// <summary>Recusa, antes de o dinheiro sair, a devolução que o pago não comporta.</summary>
    public void ConferirEstorno(long valorCents)
    {
        if (Situacao != SituacaoDaCobranca.Paga || valorCents <= 0 || EstornadoCents + valorCents > ValorCents)
        {
            throw new RegraDeNegocioException("Cobrança sem esse valor a devolver.");
        }
    }

    /// <summary>Parte devolvida ao cliente. Devolve se com ela a cobrança inteira foi devolvida.</summary>
    public bool EstornarParte(long valorCents)
    {
        ConferirEstorno(valorCents);
        EstornadoCents += valorCents;
        return EstornadoCents == ValorCents;
    }

    /// <summary>Código do pedido no Pagar.me: o id sem hífens, 32 dos 52 caracteres aceitos.</summary>
    public string Codigo => Id.ToString("N");

    /// <summary>Em centavos (<c>flat</c>, ADR 0003): a soma fecha com o valor cobrado.</summary>
    public Divisao Divisao => new(ParteDaAvaliaCents, ValorCents - ParteDaAvaliaCents);

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
