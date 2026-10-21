namespace AvaliaSales.Application.Lugares;

public sealed record Lugar(string Id, string Nome, string? Endereco);

/// <summary>Busca do negócio no Google Places (New). Guarda-se só o id do lugar.</summary>
public interface ILugares
{
    bool Configurado { get; }

    Task<IReadOnlyList<Lugar>> Buscar(string texto, CancellationToken ct);
}
