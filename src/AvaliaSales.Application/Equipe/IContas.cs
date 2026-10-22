using AvaliaSales.Domain.Acesso;

namespace AvaliaSales.Application.Equipe;

/// <summary>Os logins, que moram no ASP.NET Identity, vistos pelos casos de uso.</summary>
public interface IContas
{
    /// <summary>
    /// Recusa e-mail já usado na plataforma e senha fora da regra, com a
    /// mensagem para a tela. Sem senha, o login espera o convite.
    /// </summary>
    Task<Guid> Criar(string nome, string email, string? senha, Papel papel, Guid? revendedorId, CancellationToken ct);

    /// <summary>Põe o e-mail do convite na fila e devolve o link, que o dono também pode mandar pelo WhatsApp.</summary>
    Task<string> Convidar(Guid usuarioId, string revenda, CancellationToken ct);

    /// <summary>Quem ainda não criou a senha pelo convite.</summary>
    Task<IReadOnlySet<Guid>> SemSenha(IEnumerable<Guid> usuarios, CancellationToken ct);

    /// <summary>Desativar encerra as sessões abertas: o vendedor que saiu não continua logado.</summary>
    Task DefinirAtivo(Guid usuarioId, bool ativo, CancellationToken ct);

    Task Excluir(Guid usuarioId, CancellationToken ct);

    Task<IReadOnlyList<string>> EmailsDosDonos(Guid revendedorId, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, string>> Nomes(IEnumerable<Guid> usuarios, CancellationToken ct);

    Task<Guid?> IdPorEmail(string email, CancellationToken ct);
}
