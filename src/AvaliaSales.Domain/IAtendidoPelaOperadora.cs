namespace AvaliaSales.Domain;

/// <summary>
/// Registro do revendedor que a operadora também lê e altera, como o pedido
/// de placas que ela despacha. O filtro do EF e a política do RLS abrem a
/// tabela para ela; as demais continuam fechadas.
/// </summary>
public interface IAtendidoPelaOperadora : IDoRevendedor;
