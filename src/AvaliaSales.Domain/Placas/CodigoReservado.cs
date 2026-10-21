namespace AvaliaSales.Domain.Placas;

/// <summary>
/// Código que já existe fora da plataforma, como as placas do Sales atual da
/// Avalia. O sorteio de lote não usa código reservado: cada placa tem uma casa só.
/// </summary>
public sealed class CodigoReservado
{
    public string Codigo { get; private set; }
    public string Origem { get; private set; }

    private CodigoReservado()
    {
        Codigo = string.Empty;
        Origem = string.Empty;
    }

    public CodigoReservado(string codigo, string origem)
    {
        Codigo = CodigoDaPlaca.Normalizar(codigo) is { Length: > 0 } valido
            ? valido
            : throw new RegraDeNegocioException($"Código inválido: {codigo}.");
        Origem = origem;
    }
}
