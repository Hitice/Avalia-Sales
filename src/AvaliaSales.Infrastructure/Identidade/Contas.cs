using AvaliaSales.Application.Equipe;
using AvaliaSales.Domain;
using AvaliaSales.Domain.Acesso;
using AvaliaSales.Infrastructure.Banco;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Infrastructure.Identidade;

/// <summary>Os logins pelo UserManager, no mesmo contexto do banco: entram na transação de quem chama.</summary>
public sealed class Contas(UserManager<Usuario> usuarios, BancoDeDados banco, GestorDeSenhas senhas, TimeProvider relogio) : IContas
{
    public async Task<Guid> Criar(string nome, string email, string? senha, Papel papel, Guid? revendedorId, CancellationToken ct, bool emailConfirmado = false)
    {
        var normalizado = Emails.Normalizar(email) ?? throw new RegraDeNegocioException("Informe o e-mail.");
        nome = (nome ?? string.Empty).Trim();
        if (nome.Length is 0 or > Usuario.NomeMaximo)
        {
            throw new RegraDeNegocioException($"O nome precisa ter de 1 a {Usuario.NomeMaximo} caracteres.");
        }

        if (await usuarios.FindByEmailAsync(normalizado) is not null)
        {
            throw new RegraDeNegocioException("Este e-mail já tem cadastro na plataforma.");
        }

        var usuario = new Usuario { RevendedorId = revendedorId, Papel = papel, Nome = nome, UserName = normalizado, Email = normalizado, EmailConfirmed = emailConfirmado };
        var criado = senha is null ? await usuarios.CreateAsync(usuario) : await usuarios.CreateAsync(usuario, senha);
        return criado.Succeeded
            ? usuario.Id
            : throw new RegraDeNegocioException(string.Join(" ", criado.Errors.Select(Traduzir)));
    }

    public Task<string> Convidar(Guid usuarioId, Application.Avisos.RemetenteDoEmail revenda, CancellationToken ct) => senhas.Convidar(usuarioId, revenda, ct);

    public Task PedirConfirmacao(Guid usuarioId, CancellationToken ct) => senhas.PedirConfirmacao(usuarioId, ct);

    public async Task<IReadOnlySet<Guid>> SemSenha(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var lista = ids.Distinct().ToList();
        return (await banco.Users.AsNoTracking().Where(u => lista.Contains(u.Id) && u.PasswordHash == null).Select(u => u.Id).ToListAsync(ct)).ToHashSet();
    }

    public async Task DefinirAtivo(Guid usuarioId, bool ativo, CancellationToken ct)
    {
        var usuario = await usuarios.FindByIdAsync(usuarioId.ToString()) ?? throw new RegraDeNegocioException("Login não encontrado.");
        usuario.Ativo = ativo;
        await usuarios.UpdateAsync(usuario);
        if (!ativo)
        {
            var agora = relogio.GetUtcNow();
            await banco.Sessoes.Where(s => s.UsuarioId == usuarioId && s.EncerradaEm == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.EncerradaEm, agora), ct);
        }
    }

    public async Task Excluir(Guid usuarioId, CancellationToken ct)
    {
        var usuario = await usuarios.FindByIdAsync(usuarioId.ToString());
        if (usuario is not null)
        {
            await usuarios.DeleteAsync(usuario);
        }
    }

    /// <summary>Só e-mail confirmado: contrato e aviso não vão para endereço que ninguém provou ser do dono.</summary>
    public async Task<IReadOnlyList<string>> EmailsDosDonos(Guid revendedorId, CancellationToken ct) =>
        await banco.Users.AsNoTracking()
            .Where(u => u.RevendedorId == revendedorId && u.Papel == Papel.Dono && u.Ativo && u.EmailConfirmed && u.Email != null)
            .OrderBy(u => u.Email).Select(u => u.Email!).ToListAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, string>> Nomes(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var lista = ids.Distinct().ToList();
        return await banco.Users.AsNoTracking().Where(u => lista.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Nome, ct);
    }

    public async Task<Guid?> IdPorEmail(string email, CancellationToken ct) =>
        (await usuarios.FindByEmailAsync(email.Trim()))?.Id;

    /// <summary>As recusas do Identity que a regra de senha e de e-mail produzem, em português.</summary>
    internal static string Traduzir(IdentityError erro) => erro.Code switch
    {
        nameof(IdentityErrorDescriber.PasswordTooShort) => "A senha precisa ter 10 caracteres ou mais.",
        nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName) => "Este e-mail já tem cadastro na plataforma.",
        nameof(IdentityErrorDescriber.InvalidEmail) or nameof(IdentityErrorDescriber.InvalidUserName) => "E-mail inválido.",
        _ => erro.Description,
    };
}
