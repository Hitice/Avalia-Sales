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

    /// <summary>Se o login tem o segundo fator ligado.</summary>
    Task<bool> TemSegundoFator(Guid usuarioId, CancellationToken ct);

    Task<IReadOnlyList<string>> EmailsDosDonos(Guid revendedorId, CancellationToken ct);

    /// <summary>O primeiro dono com e-mail confirmado, na ordem de <see cref="EmailsDosDonos"/>: quem assina pela revenda.</summary>
    Task<(string Nome, string Email)?> DonoQueAssina(Guid revendedorId, CancellationToken ct);

    /// <summary>Religa os logins de um papel da revenda, como os donos na reabertura da conta.</summary>
    Task AtivarLogins(Guid revendedorId, Papel papel, CancellationToken ct);

    /// <summary>Desliga os logins da revenda, só de um papel ou de todos, e encerra as sessões abertas.</summary>
    Task DesativarLogins(Guid revendedorId, Papel? papel, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, string>> Nomes(IEnumerable<Guid> usuarios, CancellationToken ct);

    Task<Guid?> IdPorEmail(string email, CancellationToken ct);

    /// <summary>Nome e e-mail do login, para a tela de dados da conta.</summary>
    Task<(string Nome, string Email)> Ler(Guid usuarioId, CancellationToken ct);

    /// <summary>Troca o nome do login, o que aparece no cabeçalho e nas mensagens.</summary>
    Task Renomear(Guid usuarioId, string nome, CancellationToken ct);

    /// <summary>
    /// O que o cadastro público precisa saber antes de criar: se o e-mail ou o
    /// CNPJ já pertencem a uma conta de verdade e quais cadastros pendentes
    /// (nunca confirmados) ocupam um deles.
    /// </summary>
    Task<SituacaoDoCadastro> SituacaoDoCadastro(string email, string cnpj, CancellationToken ct);

    /// <summary>Login do dono sem senha, em estado pendente, e o e-mail com o link de confirmação na fila.</summary>
    Task<Guid> CriarPendente(string nome, string email, Guid revendedorId, CancellationToken ct);

    /// <summary>Link novo de confirmação do cadastro pendente; o anterior deixa de valer.</summary>
    Task EnviarConfirmacaoDoCadastro(Guid usuarioId, CancellationToken ct);

    /// <summary>Id do cadastro pendente com este e-mail, se houver.</summary>
    Task<Guid?> PendentePorEmail(string email, CancellationToken ct);

    /// <summary>Aviso a quem já tem conta: link para entrar e para recuperar a senha.</summary>
    Task AvisarQueJaTemCadastro(string email, CancellationToken ct);

    /// <summary>Aviso ao e-mail informado de que o CNPJ já tem revenda cadastrada.</summary>
    Task AvisarCnpjEmUso(string email, CancellationToken ct);

    /// <summary>Apaga cadastros pendentes (login e revenda vazia). Só apaga o que ainda é pendente.</summary>
    Task DescartarPendentes(IEnumerable<Guid> usuarios, CancellationToken ct);

    /// <summary>Descarta até <paramref name="lote"/> cadastros pendentes criados antes de <paramref name="criadosAntesDe"/>; devolve quantos.</summary>
    Task<int> DescartarPendentesVencidos(DateTimeOffset criadosAntesDe, int lote, CancellationToken ct);
}

/// <param name="EmailEmUso">O e-mail é de uma conta de verdade (confirmada ou anterior ao cadastro com confirmação).</param>
/// <param name="CnpjEmUso">O CNPJ é de uma revenda de verdade.</param>
/// <param name="Pendentes">Logins pendentes que ocupam o e-mail ou o CNPJ.</param>
public sealed record SituacaoDoCadastro(bool EmailEmUso, bool CnpjEmUso, IReadOnlyList<Guid> Pendentes);
