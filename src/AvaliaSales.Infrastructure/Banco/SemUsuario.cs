using AvaliaSales.Application;
using AvaliaSales.Domain.Acesso;

namespace AvaliaSales.Infrastructure.Banco;

/// <summary>O Worker não age em nome de ninguém: as rotinas não vendem nem consignam.</summary>
public sealed class SemUsuario : IUsuarioAtual
{
    public Guid? UsuarioId => null;

    public Papel? Papel => null;
}
