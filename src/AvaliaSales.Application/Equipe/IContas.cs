using AvaliaSales.Domain.Acesso;

namespace AvaliaSales.Application.Equipe;

/// <summary>Os logins, que moram no ASP.NET Identity, vistos pelos casos de uso.</summary>
public interface IContas
{
    /// <summary>
    /// Recusa e-mail já usado na plataforma e senha fora da regra, com a
    /// mensagem para a tela. Sem senha, o login espera o convite. Conta criada
    /// pela operadora já nasce com o e-mail confirmado: veio de quem conhece o dono.
    /// </summary>
    Task<Guid> Criar(string nome, string email, string? senha, Papel papel, Guid? revendedorId, CancellationToken ct, bool emailConfirmado = false);

    /// <summary>Põe na fila o link de confirmação do e-mail.</summary>
    Task PedirConfirmacao(Guid usuarioId, CancellationToken ct);

    /// <summary>Põe o e-mail do convite na fila e devolve o link, que o dono também pode mandar pelo WhatsApp.</summary>
    Task<string> Convidar(Guid usuarioId, Avisos.RemetenteDoEmail revenda, CancellationToken ct);

    /// <summary>Quem ainda não criou a senha pelo convite.</summary>
    Task<IReadOnlySet<Guid>> SemSenha(IEnumerable<Guid> usuarios, CancellationToken ct);

    /// <summary>Desativar encerra as sessões abertas: o vendedor que saiu não continua logado.</summary>
    Task DefinirAtivo(Guid usuarioId, bool ativo, CancellationToken ct);

    Task Excluir(Guid usuarioId, CancellationToken ct);

    Task<IReadOnlyList<string>> EmailsDosDonos(Guid revendedorId, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, string>> Nomes(IEnumerable<Guid> usuarios, CancellationToken ct);

    Task<Guid?> IdPorEmail(string email, CancellationToken ct);
}
