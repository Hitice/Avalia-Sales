namespace AvaliaSales.Domain.Placas;

public enum SituacaoDaPlaca
{
    EmEstoque = 1,
    Ativa = 2,
    Suspensa = 3,

    /// <summary>Encerrada para sempre: o código não volta a circular.</summary>
    Baixada = 4,
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
    public Guid? VendaId { get; private set; }
    public DateTimeOffset? VendidaEm { get; private set; }
    public DateTimeOffset? VenceEm { get; private set; }
    public long TotalLeituras { get; private set; }
    public DateTimeOffset? UltimaLeituraEm { get; private set; }
    public DateTimeOffset EntrouEm { get; private set; }

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
            throw new RegraDeNegocioException($"A placa {Codigo} já foi vendida e não pode ser consignada.");
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
                ? $"A placa {Codigo} foi encerrada e não volta a circular."
                : $"A placa {Codigo} já foi vendida. Para trocar o destino, use a ficha da placa.");
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
            throw new RegraDeNegocioException("Placa sem venda não vai ao ar. Registre a venda primeiro.");
        }

        if (Situacao == SituacaoDaPlaca.Baixada)
        {
            throw new RegraDeNegocioException($"A placa {Codigo} foi encerrada e não volta a circular.");
        }

        if (destino == Destino)
        {
            return false;
        }

        Destino = destino;
        return true;
    }

    /// <summary>Mais um ano a partir do vencimento atual; devolve de quando a quando.</summary>
    public (DateTimeOffset De, DateTimeOffset Ate) Renovar()
    {
        var de = VenceEm ?? throw new RegraDeNegocioException("Placa sem venda não tem o que renovar.");
        VenceEm = de.AddMonths(ValidadeMeses);
        return (de, VenceEm.Value);
    }

    /// <summary>Suspender não apaga nada: a placa volta exatamente como estava.</summary>
    public void Alternar() => Situacao = Situacao switch
    {
        SituacaoDaPlaca.Ativa => SituacaoDaPlaca.Suspensa,
        SituacaoDaPlaca.Suspensa => SituacaoDaPlaca.Ativa,
        _ => throw new RegraDeNegocioException("Só placa vendida pode ser ligada e desligada."),
    };
}
