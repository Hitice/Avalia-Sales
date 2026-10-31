namespace AvaliaSales.Domain.Vendas;

/// <summary>
/// Um destino da placa personalizada, informado na venda. A placa é feita sob encomenda: o código só nasce na ordem de
/// serviço, e aí recebe o destino da mesma posição (ADR 0032).
/// </summary>
public sealed class DestinoDaVenda : IDoRevendedor
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid RevendedorId { get; private set; }
    public Guid VendaId { get; private set; }

    /// <summary>A ordem em que o vendedor informou: o primeiro é o principal, o que vai na tag NFC.</summary>
    public int Posicao { get; private set; }

    public string Destino { get; private set; }

    private DestinoDaVenda() => Destino = string.Empty;

    public DestinoDaVenda(Guid vendaId, int posicao, string destino)
    {
        VendaId = vendaId;
        Posicao = posicao;
        Destino = destino;
    }
}
