using AvaliaSales.Application;

namespace AvaliaSales.Infrastructure.Integracoes;

/// <summary>
/// Revendedor de uma rotina do Worker, definido pelo próprio Worker a cada
/// escopo. Precisa estar definido antes da primeira consulta: a conexão grava
/// o revendedor na sessão do Postgres quando abre.
/// </summary>
public sealed class RevendedorDaExecucao : IRevendedorAtual
{
    public Guid? RevendedorId { get; set; }
}
