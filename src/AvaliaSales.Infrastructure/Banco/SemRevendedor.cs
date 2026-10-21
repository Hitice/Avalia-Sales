using AvaliaSales.Application;

namespace AvaliaSales.Infrastructure.Banco;

/// <summary>Contexto sem revendedor: migração e rotinas que não leem dado de revendedor.</summary>
public sealed class SemRevendedor : IRevendedorAtual
{
    public Guid? RevendedorId => null;

    public bool Operadora => false;
}
