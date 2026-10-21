using AvaliaSales.Domain.Acesso;

namespace AvaliaSales.Application;

/// <summary>Quem está logado, para gravar quem vendeu e liberar o que cada papel faz.</summary>
public interface IUsuarioAtual
{
    Guid? UsuarioId { get; }
    Papel? Papel { get; }
}
