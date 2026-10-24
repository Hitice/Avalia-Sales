namespace AvaliaSales.Domain.Placas;

/// <summary>
/// O código de uma placa impressa e de quem ela é. Tabela da plataforma, sem
/// RLS: a leitura do QR chega sem login e é por aqui que descobre o revendedor.
/// Guarda só identificadores; destino, venda e situação ficam na placa do
/// revendedor.
/// </summary>
public sealed class PlacaEmitida
{
    public string Codigo { get; private set; }

    /// <summary>Sequência da plataforma inteira, contínua entre lotes: dá nome ao arquivo de impressão.</summary>
    public long Sequencia { get; private set; }

    public Guid LoteId { get; private set; }
    public Guid? RevendedorId { get; private set; }

    private PlacaEmitida() => Codigo = string.Empty;

    public PlacaEmitida(string codigo, long sequencia, Guid loteId)
    {
        Codigo = CodigoDaPlaca.Normalizar(codigo) is { Length: > 0 } valido
            ? valido
            : throw new ArgumentException("Código fora do alfabeto da placa.", nameof(codigo));
        Sequencia = sequencia;
        LoteId = loteId;
    }

    public void MoverParaOLote(Guid loteId) => LoteId = loteId;

    public void EntregarA(Guid revendedorId) =>
        RevendedorId = RevendedorId is null || RevendedorId == revendedorId
            ? revendedorId
            : throw new InvalidOperationException($"A placa {Codigo} já é de outro revendedor.");
}
