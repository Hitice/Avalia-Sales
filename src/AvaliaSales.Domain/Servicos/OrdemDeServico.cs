namespace AvaliaSales.Domain.Servicos;

public enum SituacaoDaOrdem
{
    AFazer = 1,
    EmAndamento = 2,
    Entregue = 3,
    Cancelada = 4,
}

/// <summary>
/// O serviço que a Avalia executa para o cliente de uma venda paga (Perfil no
/// Google, cardápio, landing page, kit mesa). Nasce com a venda paga; a
/// operadora anda com ele e o revendedor acompanha. Só anda para a frente.
/// </summary>
public sealed class OrdemDeServico : IAtendidoPelaOperadora
{
    public const int ObservacaoMaxima = 1000;

    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <summary>Número para falar da ordem, gerado pelo banco.</summary>
    public long Numero { get; private set; }

    public Guid RevendedorId { get; private set; }
    public Guid VendaId { get; private set; }
    public Guid ProdutoDaPlataformaId { get; private set; }
    public string Produto { get; private set; }
    public string Cliente { get; private set; }

    /// <summary>Unidades a produzir, as da venda.</summary>
    public int Quantidade { get; private set; } = 1;
    public SituacaoDaOrdem Situacao { get; private set; } = SituacaoDaOrdem.AFazer;
    public string? Observacao { get; private set; }
    public DateTimeOffset CriadaEm { get; private set; }
    public DateTimeOffset? IniciadaEm { get; private set; }
    public DateTimeOffset? EntregueEm { get; private set; }

    private OrdemDeServico() => Produto = Cliente = string.Empty;

    /// <summary>O revendedor vai explícito: o pagamento pode chegar pelo contexto da operadora.</summary>
    public OrdemDeServico(Guid revendedorId, Guid vendaId, Guid produtoDaPlataformaId, string produto, string cliente, DateTimeOffset agora, int quantidade = 1)
    {
        Quantidade = quantidade;
        RevendedorId = revendedorId;
        VendaId = vendaId;
        ProdutoDaPlataformaId = produtoDaPlataformaId;
        Produto = produto;
        Cliente = cliente;
        CriadaEm = agora;
    }

    public void Iniciar(DateTimeOffset agora)
    {
        Exigir(SituacaoDaOrdem.AFazer, "Só a ordem a fazer começa.");
        Situacao = SituacaoDaOrdem.EmAndamento;
        IniciadaEm = agora;
    }

    public void Entregar(DateTimeOffset agora)
    {
        if (Situacao is not (SituacaoDaOrdem.AFazer or SituacaoDaOrdem.EmAndamento))
        {
            throw new RegraDeNegocioException("Ordem já encerrada.");
        }

        IniciadaEm ??= agora;
        Situacao = SituacaoDaOrdem.Entregue;
        EntregueEm = agora;
    }

    public void Cancelar()
    {
        if (Situacao is not (SituacaoDaOrdem.AFazer or SituacaoDaOrdem.EmAndamento))
        {
            throw new RegraDeNegocioException("Ordem já encerrada.");
        }

        Situacao = SituacaoDaOrdem.Cancelada;
    }

    /// <summary>Entrega ou cancelamento marcado por engano: a entregue volta ao andamento, a cancelada à fila.</summary>
    public void Reabrir()
    {
        (Situacao, EntregueEm) = Situacao switch
        {
            SituacaoDaOrdem.Entregue => (SituacaoDaOrdem.EmAndamento, (DateTimeOffset?)null),
            SituacaoDaOrdem.Cancelada => (SituacaoDaOrdem.AFazer, (DateTimeOffset?)null),
            _ => throw new RegraDeNegocioException("Só a ordem entregue ou cancelada se reabre."),
        };
    }

    public void Anotar(string? observacao)
    {
        observacao = observacao?.Trim();
        Observacao = string.IsNullOrEmpty(observacao)
            ? null
            : observacao.Length <= ObservacaoMaxima ? observacao : throw new RegraDeNegocioException($"Observação com até {ObservacaoMaxima} caracteres.");
    }

    private void Exigir(SituacaoDaOrdem esperada, string recusa)
    {
        if (Situacao != esperada)
        {
            throw new RegraDeNegocioException(recusa);
        }
    }
}
