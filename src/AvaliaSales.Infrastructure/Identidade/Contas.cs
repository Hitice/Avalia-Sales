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
    public async Task<bool> TemSegundoFator(Guid usuarioId, CancellationToken ct) =>
        await usuarios.FindByIdAsync(usuarioId.ToString()) is { TwoFactorEnabled: true };

    public async Task<Guid> Criar(string nome, string email, string? senha, Papel papel, Guid? revendedorId, CancellationToken ct, bool emailConfirmado = false)
    {
        var normalizado = Emails.Normalizar(email) ?? throw new RegraDeNegocioException("Informe o e-mail.");
        nome = (nome ?? string.Empty).Trim();
        if (nome.Length is 0 or > Usuario.NomeMaximo)
        {
            throw new RegraDeNegocioException($"Nome de 1 a {Usuario.NomeMaximo} caracteres.");
        }

        if (await usuarios.FindByEmailAsync(normalizado) is not null)
        {
            throw new RegraDeNegocioException("E-mail já cadastrado.");
        }

        var usuario = new Usuario { RevendedorId = revendedorId, Papel = papel, Nome = nome, UserName = normalizado, Email = normalizado, EmailConfirmed = emailConfirmado };
        var criado = senha is null ? await usuarios.CreateAsync(usuario) : await usuarios.CreateAsync(usuario, senha);
        return criado.Succeeded
            ? usuario.Id
            : throw new RegraDeNegocioException(string.Join(" ", criado.Errors.Select(Traduzir)));
    }

    public Task<string> Convidar(Guid usuarioId, Application.Avisos.RemetenteDoEmail revenda, CancellationToken ct) => senhas.Convidar(usuarioId, revenda, ct);


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

    public async Task<(string Nome, string Email)?> DonoQueAssina(Guid revendedorId, CancellationToken ct)
    {
        var dono = await banco.Users.AsNoTracking()
            .Where(u => u.RevendedorId == revendedorId && u.Papel == Papel.Dono && u.Ativo && u.EmailConfirmed && u.Email != null)
            .OrderBy(u => u.Email).Select(u => new { u.Nome, u.Email }).FirstOrDefaultAsync(ct);
        return dono is null ? null : (dono.Nome, dono.Email!);
    }

    public Task AtivarLogins(Guid revendedorId, Papel papel, CancellationToken ct) =>
        banco.Users.Where(u => u.RevendedorId == revendedorId && u.Papel == papel && !u.Ativo)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.Ativo, true), ct);

    public async Task DesativarLogins(Guid revendedorId, Papel? papel, CancellationToken ct)
    {
        var ids = await banco.Users.Where(u => u.RevendedorId == revendedorId && u.Ativo && (papel == null || u.Papel == papel))
            .Select(u => u.Id).ToListAsync(ct);
        await banco.Users.Where(u => ids.Contains(u.Id)).ExecuteUpdateAsync(s => s.SetProperty(u => u.Ativo, false), ct);
        var agora = relogio.GetUtcNow();
        await banco.Sessoes.Where(s => ids.Contains(s.UsuarioId) && s.EncerradaEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EncerradaEm, agora), ct);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> Nomes(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var lista = ids.Distinct().ToList();
        return await banco.Users.AsNoTracking().Where(u => lista.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Nome, ct);
    }

    public async Task<(string Nome, string Email)> Ler(Guid usuarioId, CancellationToken ct) =>
        await banco.Users.AsNoTracking().Where(u => u.Id == usuarioId).Select(u => new ValueTuple<string, string>(u.Nome, u.Email!)).SingleAsync(ct);

    public async Task Renomear(Guid usuarioId, string nome, CancellationToken ct)
    {
        nome = (nome ?? string.Empty).Trim();
        if (nome.Length is 0 or > Usuario.NomeMaximo)
        {
            throw new RegraDeNegocioException($"Nome de 1 a {Usuario.NomeMaximo} caracteres.");
        }

        var usuario = await banco.Users.SingleAsync(u => u.Id == usuarioId, ct);
        usuario.Nome = nome;
        await banco.SaveChangesAsync(ct);
    }

    public async Task<Guid?> IdPorEmail(string email, CancellationToken ct) =>
        (await usuarios.FindByEmailAsync(email.Trim()))?.Id;

    public async Task<SituacaoDoCadastro> SituacaoDoCadastro(string email, string cnpj, CancellationToken ct)
    {
        var pendentes = new List<Guid>();
        var doEmail = await usuarios.FindByEmailAsync(email);
        var emailEmUso = false;
        if (doEmail is not null)
        {
            if (await senhas.Pendente(doEmail.Id, ct))
            {
                pendentes.Add(doEmail.Id);
            }
            else
            {
                emailEmUso = true;
            }
        }

        var cnpjEmUso = false;
        if (await banco.Revendedores.AsNoTracking().Where(r => r.Cnpj == cnpj).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct) is { } revendedorId)
        {
            // Só é pendente a revenda em que todos os logins são cadastros por confirmar; o resto é conta de verdade.
            var logins = await banco.Users.AsNoTracking().Where(u => u.RevendedorId == revendedorId).Select(u => u.Id).ToListAsync(ct);
            var todosPendentes = logins.Count > 0;
            foreach (var id in logins)
            {
                todosPendentes &= await senhas.Pendente(id, ct);
            }

            if (todosPendentes)
            {
                pendentes.AddRange(logins.Where(id => !pendentes.Contains(id)));
            }
            else
            {
                cnpjEmUso = true;
            }
        }

        return new SituacaoDoCadastro(emailEmUso, cnpjEmUso, pendentes);
    }

    public async Task<Guid> CriarPendente(string nome, string email, Guid revendedorId, CancellationToken ct)
    {
        var id = await Criar(nome, email, null, Papel.Dono, revendedorId, ct);
        await senhas.PedirConfirmacaoDoCadastro(id, ct);
        return id;
    }

    public Task EnviarConfirmacaoDoCadastro(Guid usuarioId, CancellationToken ct) => senhas.PedirConfirmacaoDoCadastro(usuarioId, ct);

    public async Task<Guid?> PendentePorEmail(string email, CancellationToken ct) =>
        await usuarios.FindByEmailAsync(email) is { } usuario && await senhas.Pendente(usuario.Id, ct) ? usuario.Id : null;

    public Task AvisarQueJaTemCadastro(string email, CancellationToken ct) => senhas.AvisarQueJaTemCadastro(email, ct);

    public Task AvisarCnpjEmUso(string email, CancellationToken ct) => senhas.AvisarCnpjEmUso(email, ct);

    public async Task DescartarPendentes(IEnumerable<Guid> ids, CancellationToken ct)
    {
        foreach (var id in ids.Distinct())
        {
            // Confere de novo: o login pode ter confirmado entre a leitura e agora, e esse não se apaga.
            if (!await senhas.Pendente(id, ct))
            {
                continue;
            }

            var revendedorId = await banco.Users.Where(u => u.Id == id).Select(u => u.RevendedorId).SingleAsync(ct);
            await banco.Users.Where(u => u.Id == id).ExecuteDeleteAsync(ct);
            if (revendedorId is { } rid)
            {
                await banco.Revendedores.Where(r => r.Id == rid && r.ContratoAssinadoEm == null && !banco.Users.Any(u => u.RevendedorId == rid))
                    .ExecuteDeleteAsync(ct);
            }
        }
    }

    public async Task<int> DescartarPendentesVencidos(DateTimeOffset criadosAntesDe, int lote, CancellationToken ct)
    {
        var ids = await banco.Users.AsNoTracking()
            .Where(u => !u.EmailConfirmed && u.Papel == Papel.Dono
                && banco.LinksDeSenha.Any(l => l.UsuarioId == u.Id && l.Finalidade == FinalidadeDoLink.CadastroPendente)
                && banco.Revendedores.Any(r => r.Id == u.RevendedorId && r.CriadoEm < criadosAntesDe))
            .OrderBy(u => u.Id).Select(u => u.Id).Take(lote).ToListAsync(ct);
        await DescartarPendentes(ids, ct);
        return ids.Count;
    }

    /// <summary>As recusas do Identity que a regra de senha e de e-mail produzem, em português.</summary>
    internal static string Traduzir(IdentityError erro) => erro.Code switch
    {
        nameof(IdentityErrorDescriber.PasswordTooShort) => "Senha curta. Use 10 caracteres ou mais.",
        nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName) => "E-mail já cadastrado.",
        nameof(IdentityErrorDescriber.InvalidEmail) or nameof(IdentityErrorDescriber.InvalidUserName) => "E-mail inválido.",
        _ => erro.Description,
    };
}
