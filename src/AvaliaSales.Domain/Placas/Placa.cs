namespace AvaliaSales.Domain.Placas;

public enum SituacaoDaPlaca
{
    EmEstoque = 1,
    Ativa = 2,
    Suspensa = 3,

    /// <summary>Encerrada para sempre: o código não volta a circular.</summary>
    Baixada = 4,
}

/// <summary>Por que a placa saiu de circulação.</summary>
public enum MotivoDaBaixa
{
    /// <summary>Trocada por outra placa na mesma venda.</summary>
    Troca = 1,

    /// <summary>Saiu errada da gráfica: o acrílico volta com um QR novo, sem perda.</summary>
    DefeitoDeImpressao = 2,

    Perdida = 3,
    Quebrada = 4,

    /// <summary>Código gerado que nunca foi impresso.</summary>
    NaoImpressa = 5,

    /// <summary>O lote foi arquivado; desarquivar devolve a placa ao estoque.</summary>
    LoteArquivado = 6,

    /// <summary>A revenda encerrou a conta.</summary>
    Encerramento = 7,
}

/// <summary>A situação gravada mais o prazo: carência e vencida saem da data, não de coluna.</summary>
public enum EstadoDaPlaca
{
    EmEstoque,
    Ativa,
    Carencia,
    Vencida,
    Suspensa,
    Baixada,
}

/// <summary>
/// A placa no estoque do revendedor, do lote ativado até a baixa. Nunca é
/// excluída: o código está impresso e precisa continuar explicável.
/// </summary>
public sealed class Placa : IDoRevendedor
{
    public const int ValidadeMeses = 12;
    public const int CarenciaDias = 30;

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid RevendedorId { get; private set; }
    public string Codigo { get; private set; }
    public long Sequencia { get; private set; }
    public Guid LoteId { get; private set; }

    /// <summary>Custo congelado na ativação: o preço do pacote dividido pelas placas.</summary>
    public long CustoCents { get; private set; }

    public SituacaoDaPlaca Situacao { get; private set; } = SituacaoDaPlaca.EmEstoque;

    /// <summary>Vendedor com a placa em mãos. Consignação é só dentro do revendedor.</summary>
    public Guid? ConsignadaA { get; private set; }

    public string? Destino { get; private set; }

    /// <summary>Fora do ar porque o teste gratuito venceu sem pagamento: só o pagamento devolve, e não o botão de ligar.</summary>
    public bool SuspensaPorTeste { get; private set; }

    public Guid? VendaId { get; private set; }
    public DateTimeOffset? VendidaEm { get; private set; }
    public DateTimeOffset? VenceEm { get; private set; }

    /// <summary>O vencimento sobre o qual a revenda e o cliente já foram avisados, e a menor janela (em dias) avisada para ele.</summary>
    public DateTimeOffset? VencimentoAvisado { get; private set; }

    public int? JanelaAvisada { get; private set; }

    /// <summary>Vencimento novo (venda ou renovação) recomeça os avisos; no mesmo vencimento, só a janela mais curta que ainda não saiu.</summary>
    public bool PrecisaAvisarDoVencimento(int janelaEmDias) =>
        VenceEm is not null && (VencimentoAvisado != VenceEm || JanelaAvisada > janelaEmDias);

    public void AvisouDoVencimento(int janelaEmDias)
    {
        VencimentoAvisado = VenceEm;
        JanelaAvisada = janelaEmDias;
    }
    public long TotalLeituras { get; private set; }
    public DateTimeOffset? UltimaLeituraEm { get; private set; }
    public DateTimeOffset EntrouEm { get; private set; }

    public MotivoDaBaixa? MotivoDaBaixa { get; private set; }
    public DateTimeOffset? BaixadaEm { get; private set; }

    /// <summary>
    /// Quando a placa no ar passou para a revenda da Avalia, no encerramento da
    /// revenda que a vendeu. A venda fica com a revenda antiga, e estes dois
    /// campos guardam o que a renovação precisa dela.
    /// </summary>
    public DateTimeOffset? TransferidaEm { get; private set; }

    public Guid? ClienteDaTransferenciaId { get; private set; }
    public long? ValorDaVendaCents { get; private set; }

    private Placa() => Codigo = string.Empty;

    public Placa(PlacaEmitida emitida, long custoCents, DateTimeOffset agora)
    {
        Codigo = emitida.Codigo;
        Sequencia = emitida.Sequencia;
        LoteId = emitida.LoteId;
        CustoCents = custoCents >= 0 ? custoCents : throw new ArgumentOutOfRangeException(nameof(custoCents));
        EntrouEm = agora;
    }

    public DateTimeOffset? FimDaCarencia => VenceEm?.AddDays(CarenciaDias);

    /// <summary>
    /// A placa que vem de outro sistema, com a situação e as datas de lá. Só a
    /// importação usa: no balcão, a placa nasce em estoque e vai ao ar pela venda.
    /// </summary>
    public static Placa Importada(PlacaEmitida emitida, long custoCents, DateTimeOffset entrouEm, SituacaoDaPlaca situacao,
        string? destino, DateTimeOffset? vendidaEm, DateTimeOffset? venceEm, long totalLeituras)
    {
        if (situacao is SituacaoDaPlaca.Ativa or SituacaoDaPlaca.Suspensa && vendidaEm is null)
        {
            throw new RegraDeNegocioException($"a placa {emitida.Codigo} está no ar sem venda; o arquivo precisa da data da venda");
        }

        if (situacao == SituacaoDaPlaca.EmEstoque && vendidaEm is not null)
        {
            throw new RegraDeNegocioException($"a placa {emitida.Codigo} está em estoque com data de venda");
        }

        return new Placa(emitida, custoCents, entrouEm)
        {
            Situacao = situacao,
            Destino = destino,
            VendidaEm = vendidaEm,
            VenceEm = venceEm,
            TotalLeituras = totalLeituras,
        };
    }

    /// <summary>Liga a venda importada à placa importada; cada uma precisa do id da outra.</summary>
    public void LigarVendaImportada(Guid vendaId) =>
        VendaId = VendaId is null && VendidaEm is not null ? vendaId : throw new InvalidOperationException($"A placa {Codigo} já tem venda ou não foi vendida.");

    public EstadoDaPlaca Estado(DateTimeOffset agora) => EstadoDe(Situacao, VenceEm, agora);

    /// <summary>Também para a lista, que lê só as colunas e não a placa inteira.</summary>
    public static EstadoDaPlaca EstadoDe(SituacaoDaPlaca situacao, DateTimeOffset? venceEm, DateTimeOffset agora) => situacao switch
    {
        SituacaoDaPlaca.EmEstoque => EstadoDaPlaca.EmEstoque,
        SituacaoDaPlaca.Suspensa => EstadoDaPlaca.Suspensa,
        SituacaoDaPlaca.Baixada => EstadoDaPlaca.Baixada,
        _ when venceEm is null || agora <= venceEm => EstadoDaPlaca.Ativa,
        _ when agora <= venceEm.Value.AddDays(CarenciaDias) => EstadoDaPlaca.Carencia,
        _ => EstadoDaPlaca.Vencida,
    };

    /// <summary>A carência existe para o cliente renovar sem a placa sair do ar.</summary>
    public bool Redireciona(DateTimeOffset agora) =>
        Destino is not null && Estado(agora) is EstadoDaPlaca.Ativa or EstadoDaPlaca.Carencia;

    public void Consignar(Guid vendedorId)
    {
        if (Situacao != SituacaoDaPlaca.EmEstoque)
        {
            throw new RegraDeNegocioException($"Placa {Codigo} já vendida. Consigne outra.");
        }

        ConsignadaA = vendedorId;
    }

    public void Devolver()
    {
        if (Situacao == SituacaoDaPlaca.EmEstoque)
        {
            ConsignadaA = null;
        }
    }

    /// <summary>
    /// A placa só vai ao ar com venda. O prazo sai da constante, e não do
    /// formulário: validade digitada à mão um dia sai com dois anos por engano.
    /// </summary>
    public void Vender(Guid vendaId, string destino, DateTimeOffset agora)
    {
        if (Situacao != SituacaoDaPlaca.EmEstoque || VendaId is not null)
        {
            throw new RegraDeNegocioException(Situacao == SituacaoDaPlaca.Baixada
                ? $"Placa {Codigo} encerrada."
                : $"Placa {Codigo} já vendida. Troque o destino na ficha da placa.");
        }

        VendaId = vendaId;
        VendidaEm = agora;
        VenceEm = agora.AddMonths(ValidadeMeses);
        Destino = destino;
        Situacao = SituacaoDaPlaca.Ativa;
        ConsignadaA = null;
    }

    /// <summary>Troca o destino de placa vendida. Placa sem venda não vai ao ar.</summary>
    public bool Apontar(string destino)
    {
        if (VendaId is null)
        {
            throw new RegraDeNegocioException("Placa sem venda não vai ao ar. Registre a venda.");
        }

        if (Situacao == SituacaoDaPlaca.Baixada)
        {
            throw new RegraDeNegocioException($"Placa {Codigo} encerrada.");
        }

        if (destino == Destino)
        {
            return false;
        }

        Destino = destino;
        return true;
    }

    /// <summary>
    /// O teste gratuito que virou venda: os 12 meses contam do pagamento, e a
    /// placa que saiu do ar quando o teste venceu volta.
    /// </summary>
    public void IniciarVigencia(DateTimeOffset pagoEm, bool voltarAoAr)
    {
        if (VendaId is null || Situacao == SituacaoDaPlaca.Baixada)
        {
            return;
        }

        VenceEm = pagoEm.AddMonths(ValidadeMeses);
        SuspensaPorTeste = false;
        if (voltarAoAr && Situacao == SituacaoDaPlaca.Suspensa)
        {
            Situacao = SituacaoDaPlaca.Ativa;
        }
    }

    /// <summary>
    /// O teste venceu sem pagamento: a placa sai do ar até pagar ou voltar. Já suspensa à mão, ela fica marcada também,
    /// senão o botão de ligar a devolveria ao ar sem pagamento.
    /// </summary>
    public void TirarDoArPorTeste()
    {
        if (Situacao is SituacaoDaPlaca.Ativa or SituacaoDaPlaca.Suspensa)
        {
            Situacao = SituacaoDaPlaca.Suspensa;
            SuspensaPorTeste = true;
        }
    }

    /// <summary>Mais um ano a partir do vencimento atual; devolve de quando a quando.</summary>
    public (DateTimeOffset De, DateTimeOffset Ate) Renovar()
    {
        var de = VenceEm ?? throw new RegraDeNegocioException("Placa sem venda não renova.");
        VenceEm = de.AddMonths(ValidadeMeses);
        return (de, VenceEm.Value);
    }

    /// <summary>A renovação desfeita devolve o vencimento de antes, se nada o mudou depois dela.</summary>
    public void DesfazerRenovacao(DateTimeOffset de, DateTimeOffset ate)
    {
        if (VenceEm != ate)
        {
            throw new RegraDeNegocioException($"Vencimento da placa {Codigo} mudou depois da renovação.");
        }

        VenceEm = de;
    }

    /// <summary>Venda desfeita: a placa volta ao estoque e sai do ar. Placa sem venda não vai ao ar.</summary>
    public void DesfazerVenda()
    {
        if (Situacao == SituacaoDaPlaca.Baixada)
        {
            throw new RegraDeNegocioException($"Placa {Codigo} encerrada.");
        }

        Situacao = SituacaoDaPlaca.EmEstoque;
        SuspensaPorTeste = false;
        VendaId = null;
        VendidaEm = null;
        VenceEm = null;
        Destino = null;
    }

    /// <summary>
    /// Troca de placa (a antiga quebrou, sumiu, foi gravada errada): a venda,
    /// o destino e o vencimento passam para esta, e a antiga é encerrada para
    /// sempre. O código antigo não volta a circular.
    /// </summary>
    public void ReceberVendaDe(Placa antiga, MotivoDaBaixa? motivo = null, DateTimeOffset? agora = null)
    {
        if (motivo is not (null or Placas.MotivoDaBaixa.Troca or Placas.MotivoDaBaixa.DefeitoDeImpressao or Placas.MotivoDaBaixa.Quebrada or Placas.MotivoDaBaixa.Perdida))
        {
            throw new RegraDeNegocioException("Motivo da troca inválido.");
        }

        if (Situacao != SituacaoDaPlaca.EmEstoque || VendaId is not null)
        {
            throw new RegraDeNegocioException($"Placa {Codigo} indisponível no estoque.");
        }

        if (antiga.VendaId is null || antiga.Situacao == SituacaoDaPlaca.Baixada)
        {
            throw new RegraDeNegocioException($"Placa {antiga.Codigo} sem venda para passar.");
        }

        VendaId = antiga.VendaId;
        VendidaEm = antiga.VendidaEm;
        VenceEm = antiga.VenceEm;
        Destino = antiga.Destino;
        Situacao = antiga.Situacao == SituacaoDaPlaca.Suspensa ? SituacaoDaPlaca.Suspensa : SituacaoDaPlaca.Ativa;
        SuspensaPorTeste = antiga.SuspensaPorTeste;
        antiga.SuspensaPorTeste = false;
        ConsignadaA = null;
        // A antiga guarda a venda de onde saiu: a ficha conta a história, e a baixa dela não se desfaz.
        antiga.Situacao = SituacaoDaPlaca.Baixada;
        antiga.MotivoDaBaixa = motivo ?? Placas.MotivoDaBaixa.Troca;
        antiga.BaixadaEm = agora;
    }

    /// <summary>
    /// Baixa de placa que não foi vendida: defeito, perda, quebra, código não
    /// impresso, lote arquivado. A placa vendida sai pela troca ou pelo cancelamento.
    /// </summary>
    public void Baixar(MotivoDaBaixa motivo, DateTimeOffset agora)
    {
        if (Situacao != SituacaoDaPlaca.EmEstoque || VendaId is not null)
        {
            throw new RegraDeNegocioException($"Placa {Codigo} indisponível no estoque.");
        }

        if (motivo is Placas.MotivoDaBaixa.Troca or Placas.MotivoDaBaixa.Encerramento || !Enum.IsDefined(motivo))
        {
            throw new RegraDeNegocioException("Motivo de baixa inválido para placa em estoque.");
        }

        Situacao = SituacaoDaPlaca.Baixada;
        MotivoDaBaixa = motivo;
        BaixadaEm = agora;
        ConsignadaA = null;
    }

    /// <summary>Baixa lançada por engano, ou lote desarquivado: a placa volta ao estoque.</summary>
    public void DesfazerBaixa()
    {
        if (Situacao != SituacaoDaPlaca.Baixada || VendaId is not null || MotivoDaBaixa is null or Placas.MotivoDaBaixa.Troca or Placas.MotivoDaBaixa.Encerramento)
        {
            throw new RegraDeNegocioException($"Baixa da placa {Codigo} não se desfaz.");
        }

        Situacao = SituacaoDaPlaca.EmEstoque;
        MotivoDaBaixa = null;
        BaixadaEm = null;
    }

    public void PassarAoCliente(Guid clienteId) => ClienteDaTransferenciaId = ClienteDaTransferenciaId is null ? null : clienteId;

    /// <summary>Volta ao lote em que foi impressa, quando veio de outro sistema num lote só.</summary>
    public void MoverParaOLote(Guid loteId) => LoteId = loteId;

    /// <summary>Suspender não apaga nada: a placa volta exatamente como estava. A suspensa pelo teste vencido só volta pelo pagamento.</summary>
    public void Alternar() => Situacao = Situacao switch
    {
        SituacaoDaPlaca.Suspensa when SuspensaPorTeste => throw new RegraDeNegocioException($"Placa {Codigo} fora do ar pelo teste vencido. Receba o pagamento."),
        SituacaoDaPlaca.Ativa => SituacaoDaPlaca.Suspensa,
        SituacaoDaPlaca.Suspensa => SituacaoDaPlaca.Ativa,
        _ => throw new RegraDeNegocioException("Só placa vendida se liga e desliga."),
    };
}
