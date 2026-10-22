using AvaliaSales.Domain;
using AvaliaSales.Domain.Acesso;
using AvaliaSales.Domain.Auditoria;
using AvaliaSales.Infrastructure.Banco;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Infrastructure.Identidade;

public sealed record ChaveDoAutenticador(string Chave, string Uri);

/// <summary>
/// Segundo fator por app autenticador (TOTP, RFC 6238), para quem mexe em
/// dinheiro e dados: dono e operadora. Depois da senha, a entrada devolve um
/// desafio de uso único (5 minutos) e só emite o token com o código. Código
/// errado conta para o bloqueio de 5 erros, como a senha.
/// </summary>
public sealed class GestorDeSegundoFator(BancoDeDados banco, UserManager<Usuario> usuarios, Application.Auditoria.Auditoria auditoria, TimeProvider relogio)
{
    public const int CodigosDeRecuperacao = 10;
    private const string Emissor = "Avalia Sales";

    public static bool Pode(Usuario usuario) => usuario.Papel is Papel.Dono or Papel.Operadora;

    /// <summary>Chave nova para o app; só vale depois de confirmada com um código.</summary>
    public async Task<ChaveDoAutenticador> Preparar(Guid usuarioId)
    {
        var usuario = await Achar(usuarioId);
        if (usuario.TwoFactorEnabled)
        {
            throw new RegraDeNegocioException("O segundo fator já está ligado.");
        }

        await usuarios.ResetAuthenticatorKeyAsync(usuario);
        var chave = await usuarios.GetAuthenticatorKeyAsync(usuario) ?? throw new InvalidOperationException("Chave do autenticador não gerada.");
        var conta = Uri.EscapeDataString($"{Emissor}:{usuario.Email}");
        return new ChaveDoAutenticador(chave, $"otpauth://totp/{conta}?secret={chave}&issuer={Uri.EscapeDataString(Emissor)}&digits=6");
    }

    /// <summary>Liga com o primeiro código do app e devolve os códigos de recuperação, mostrados uma vez só.</summary>
    public async Task<IReadOnlyList<string>> Ligar(Guid usuarioId, string? codigo)
    {
        var usuario = await Achar(usuarioId);
        if (usuario.TwoFactorEnabled || !await CodigoDoApp(usuario, codigo))
        {
            throw new RegraDeNegocioException("Código não confere. Confira o horário do celular e tente o código novo.");
        }

        await usuarios.SetTwoFactorEnabledAsync(usuario, true);
        Auditar(usuario, AcaoAuditada.SegundoFatorLigado);
        await banco.SaveChangesAsync();
        return (await usuarios.GenerateNewTwoFactorRecoveryCodesAsync(usuario, CodigosDeRecuperacao))?.ToList() ?? [];
    }

    public async Task Desligar(Guid usuarioId, string? codigo)
    {
        var usuario = await Achar(usuarioId);
        if (!usuario.TwoFactorEnabled || !await Confere(usuario, codigo))
        {
            throw new RegraDeNegocioException("Código não confere.");
        }

        await usuarios.SetTwoFactorEnabledAsync(usuario, false);
        await usuarios.ResetAuthenticatorKeyAsync(usuario);
        Auditar(usuario, AcaoAuditada.SegundoFatorDesligado);
        await banco.SaveChangesAsync();
    }

    /// <summary>O desafio da entrada: o token só sai com ele e o código.</summary>
    public async Task<string> Desafiar(Usuario usuario, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        await banco.LinksDeSenha.Where(l => l.UsuarioId == usuario.Id && l.Finalidade == FinalidadeDoLink.SegundoFator && l.EncerradoEm == null)
            .ExecuteUpdateAsync(l => l.SetProperty(x => x.EncerradoEm, agora), ct);
        var (desafio, token) = LinkDeSenha.Emitir(usuario.Id, FinalidadeDoLink.SegundoFator, agora);
        banco.LinksDeSenha.Add(desafio);
        await banco.SaveChangesAsync(ct);
        return token;
    }

    /// <summary>Código do app ou de recuperação. Recusa igual para desafio vencido e código errado.</summary>
    public async Task<Usuario> Responder(string? desafio, string? codigo, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        var hash = Sessao.Hash(desafio ?? string.Empty);
        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        var link = await banco.LinksDeSenha.FromSql($"select * from links_de_senha where hash_do_token = {hash} for update").SingleOrDefaultAsync(ct);
        var usuario = link is { Finalidade: FinalidadeDoLink.SegundoFator } && link.Valido(agora)
            ? await usuarios.FindByIdAsync(link.UsuarioId.ToString())
            : null;
        if (usuario is not { Ativo: true } || await usuarios.IsLockedOutAsync(usuario))
        {
            throw new RegraDeNegocioException("Entre de novo com a senha.");
        }

        if (!await Confere(usuario, codigo))
        {
            await usuarios.AccessFailedAsync(usuario);
            await transacao.CommitAsync(ct);
            throw new RegraDeNegocioException("Código não confere.");
        }

        await usuarios.ResetAccessFailedCountAsync(usuario);
        link!.Encerrar(agora);
        await banco.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
        return usuario;
    }

    private async Task<bool> Confere(Usuario usuario, string? codigo)
    {
        var limpo = (codigo ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        if (limpo.Length == 6 && limpo.All(char.IsAsciiDigit))
        {
            return await CodigoDoApp(usuario, limpo);
        }

        return limpo.Length > 0 && (await usuarios.RedeemTwoFactorRecoveryCodeAsync(usuario, (codigo ?? string.Empty).Trim())).Succeeded;
    }

    private Task<bool> CodigoDoApp(Usuario usuario, string? codigo) =>
        usuarios.VerifyTwoFactorTokenAsync(usuario, usuarios.Options.Tokens.AuthenticatorTokenProvider, (codigo ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal));

    /// <summary>A operadora não tem revendedor, e a auditoria é por revendedor: fica só para o dono.</summary>
    private void Auditar(Usuario usuario, AcaoAuditada acao)
    {
        if (usuario.RevendedorId is not null)
        {
            auditoria.Registrar(acao, $"Login {usuario.Nome}", usuario.Id.ToString());
        }
    }

    private async Task<Usuario> Achar(Guid id)
    {
        var usuario = await usuarios.FindByIdAsync(id.ToString()) ?? throw new RegraDeNegocioException("Login não encontrado.");
        return Pode(usuario) ? usuario : throw new RegraDeNegocioException("O segundo fator é para o dono e a operadora.");
    }
}
