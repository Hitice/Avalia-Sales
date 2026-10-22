using AvaliaSales.Domain.Placas;

namespace AvaliaSales.Domain.Links;

/// <summary>
/// O código de um link curto e de quem ele é. Tabela da plataforma, sem RLS,
/// como a das placas emitidas: o clique chega sem login. Nunca é apagada, e o
/// código não volta ao sorteio.
/// </summary>
public sealed class CodigoDeLink
{
    public string Codigo { get; private set; }
    public Guid RevendedorId { get; private set; }

    private CodigoDeLink() => Codigo = string.Empty;

    public CodigoDeLink(string codigo, Guid revendedorId)
    {
        Codigo = CodigoDaPlaca.Normalizar(codigo) is { Length: > 0 } valido
            ? valido
            : throw new ArgumentException("Código fora do alfabeto.", nameof(codigo));
        RevendedorId = revendedorId;
    }
}
