namespace AvaliaSales.Domain;

/// <summary>
/// Registro que pertence a um revendedor. A infraestrutura preenche
/// RevendedorId na gravação e filtra toda leitura por ele; o domínio nunca
/// escolhe o revendedor.
/// </summary>
public interface IDoRevendedor
{
    Guid RevendedorId { get; }
}
