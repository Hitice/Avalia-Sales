namespace AvaliaSales.Domain.Placas;

/// <summary>
/// Leituras de uma placa num dia. Por dia, e não por leitura: placa em porta
/// movimentada geraria milhares de linhas por mês sem responder melhor à
/// pergunta do cliente, que é quantas leituras teve no mês. Gravada por SQL
/// direto, com soma atômica.
/// </summary>
public sealed class LeituraDoDia : IDoRevendedor
{
    public Guid PlacaId { get; private set; }
    public DateOnly Dia { get; private set; }
    public Guid RevendedorId { get; private set; }
    public int Total { get; private set; }

    private LeituraDoDia()
    {
    }
}
