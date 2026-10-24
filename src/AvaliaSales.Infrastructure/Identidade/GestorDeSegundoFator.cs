using AvaliaSales.Application.Avisos;
using AvaliaSales.Domain;
using AvaliaSales.Domain.Acesso;
using AvaliaSales.Domain.Auditoria;
using AvaliaSales.Infrastructure.Banco;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Infrastructure.Identidade;

public sealed record ChaveDoAutenticador(string Chave, string Uri);

public enum MeioDoSegundoFator
{
    App,
    Email,
}

/// <summary>O desafio da entrada e para onde foi o código, quando foi por e-mail (mascarado).</summary>
public sealed record DesafioDaEntrada(string Desafio, MeioDoSegundoFator Meio, string? Destino);

/// <summary>
/// Segundo fator para quem mexe em dinheiro e dados: dono e operadora. Pelo app
/// autenticador (TOTP, RFC 6238) ou, para quem não quer instalar app, por
/// código no e-mail confirmado, que protege menos: quem tem a senha às vezes
/// tem o e-mail. Depois da senha, a entrada devolve um desafio de uso único (5
/// minutos) e só emite o token com o código. Código errado conta para o
/// bloqueio de 5 erros, como a senha.
/// </summary>
public sealed class GestorDeSegundoFator(BancoDeDados banco, UserManager<Usuario> usuarios, FilaDeEmails emails,
    Application.Auditoria.Auditoria auditoria, TimeProvider relogio)
{
    public const int CodigosDeRecuperacao = 10;
    private const string Emissor = "Avalia Sales";

    public static bool Pode(Usuario usuario) => usuario.Papel is Papel.Dono or Papel.Operadora;

    public static MeioDoSegundoFator Meio(Usuario usuario) => usuario.SegundoFatorPorEmail ? MeioDoSegundoFator.Email : MeioDoSegundoFator.App;

    /// <summary>Chave nova para o app; só vale depois de confirmada com um código.</summary>
    public async Task<ChaveDoAutenticador> Preparar(Guid usuarioId)
    {
        var usuario = await AcharDesligado(usuarioId);
        await usuarios.ResetAuthenticatorKeyAsync(usuario);
        var chave = await usuarios.GetAuthenticatorKeyAsync(usuario) ?? throw new InvalidOperationException("Chave do autenticador não gerada.");
        var conta = Uri.EscapeDataString($"{Emissor}:{usuario.Email}");
        return new ChaveDoAutenticador(chave, $"otpauth://totp/{conta}?secret={chave}&issuer={Uri.EscapeDataString(Emissor)}&digits=6");
    }

    /// <summary>Liga com o primeiro código do app e devolve os códigos de recuperação, mostrados uma vez só.</summary>
    public async Task<IReadOnlyList<string>> Ligar(Guid usuarioId, string? codigo)
    {
        var usuario = await AcharDesligado(usuarioId);
        if (!await CodigoDoApp(usuario, codigo))
        {
            throw new RegraDeNegocioException("Código não confere. Confira o horário do celular e tente o código novo.");
        }

        return await Ativar(usuario, porEmail: false);
    }

    /// <summary>
    /// Código no e-mail para ligar (prova que a caixa é do dono) ou, com o fator
    /// por e-mail já ligado, para desligar.
    /// </summary>
    public async Task PrepararEmail(Guid usuarioId, CancellationToken ct)
    {
        var usuario = await Achar(usuarioId);
        if (usuario.TwoFactorEnabled && !usuario.SegundoFatorPorEmail)
        {
            throw new RegraDeNegocioException("O segundo fator já está ligado pelo app.");
        }

        if (!usuario.EmailConfirmed)
        {
            throw new RegraDeNegocioException("Confirme o e-mail antes de usar o código por e-mail.");
        }

        await MandarCodigo(usuario, $"ligar:{Guid.CreateVersion7():N}", ct);
    }

    public async Task<IReadOnlyList<string>> LigarPorEmail(Guid usuarioId, string? codigo)
    {
        var usuario = await AcharDesligado(usuarioId);
        if (!usuario.EmailConfirmed || !await CodigoDoEmail(usuario, codigo))
        {
            throw new RegraDeNegocioException("Código não confere. Peça outro e use o mais recente.");
        }

        return await Ativar(usuario, porEmail: true);
    }

    /// <summary>
    /// Desligar começa pelo botão: um código novo vai para o e-mail cadastrado,
    /// tenha o fator sido ligado pelo app ou pelo e-mail. Quem tem só a sessão
    /// aberta, sem a caixa de e-mail, não desliga.
    /// </summary>
    public async Task PrepararDesligamento(Guid usuarioId, CancellationToken ct)
    {
        var usuario = await Achar(usuarioId);
        if (!usuario.TwoFactorEnabled)
        {
            throw new RegraDeNegocioException("A autenticação de dois fatores já está desativada.");
        }

        if (!usuario.EmailConfirmed || usuario.Email is null)
        {
            throw new RegraDeNegocioException("Confirme o e-mail para receber o código.");
        }

        await MandarCodigo(usuario, $"desligar:{Guid.CreateVersion7():N}", ct);
    }

    /// <summary>Com o código do e-mail; o do app e os de recuperação também valem.</summary>
    public async Task Desligar(Guid usuarioId, string? codigo)
    {
        var usuario = await Achar(usuarioId);
        if (!usuario.TwoFactorEnabled || !(usuario.EmailConfirmed && await CodigoDoEmail(usuario, codigo) || await Confere(usuario, codigo)))
        {
            throw new RegraDeNegocioException("Código não confere.");
        }

        await usuarios.SetTwoFactorEnabledAsync(usuario, false);
        await usuarios.ResetAuthenticatorKeyAsync(usuario);
        usuario.SegundoFatorPorEmail = false;
        await usuarios.UpdateAsync(usuario);
        Auditar(usuario, AcaoAuditada.SegundoFatorDesligado);
        await banco.SaveChangesAsync();
    }

    /// <summary>O desafio da entrada: o token só sai com ele e o código. Por e-mail, o código já sai junto.</summary>
    public async Task<DesafioDaEntrada> Desafiar(Usuario usuario, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        await banco.LinksDeSenha.Where(l => l.UsuarioId == usuario.Id && l.Finalidade == FinalidadeDoLink.SegundoFator && l.EncerradoEm == null)
            .ExecuteUpdateAsync(l => l.SetProperty(x => x.EncerradoEm, agora), ct);
        var (desafio, token) = LinkDeSenha.Emitir(usuario.Id, FinalidadeDoLink.SegundoFator, agora);
        banco.LinksDeSenha.Add(desafio);
        await banco.SaveChangesAsync(ct);
        if (usuario.SegundoFatorPorEmail)
        {
            await MandarCodigo(usuario, $"fator:{desafio.Id:N}", ct);
        }

        return new DesafioDaEntrada(token, Meio(usuario), usuario.SegundoFatorPorEmail ? Mascarar(usuario.Email!) : null);
    }

    /// <summary>Código novo no e-mail para o mesmo desafio, que continua valendo até os 5 minutos.</summary>
    public async Task Reenviar(string? desafio, CancellationToken ct)
    {
        var (link, usuario) = await DesafioValido(desafio, ct);
        if (!usuario.SegundoFatorPorEmail)
        {
            throw new RegraDeNegocioException("Use o código do app autenticador.");
        }

        await MandarCodigo(usuario, $"fator:{link.Id:N}:{Guid.CreateVersion7():N}", ct);
    }

    /// <summary>Código do app, do e-mail ou de recuperação. Recusa igual para desafio vencido e código errado.</summary>
    public async Task<Usuario> Responder(string? desafio, string? codigo, CancellationToken ct)
    {
        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        var (link, usuario) = await DesafioValido(desafio, ct);
        if (await usuarios.IsLockedOutAsync(usuario))
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
        link.Encerrar(relogio.GetUtcNow());
        await banco.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
        return usuario;
    }

    private async Task<(LinkDeSenha Link, Usuario Usuario)> DesafioValido(string? desafio, CancellationToken ct)
    {
        var hash = Sessao.Hash(desafio ?? string.Empty);
        var link = await banco.LinksDeSenha.FromSql($"select * from links_de_senha where hash_do_token = {hash} for update").SingleOrDefaultAsync(ct);
        var usuario = link is { Finalidade: FinalidadeDoLink.SegundoFator } && link.Valido(relogio.GetUtcNow())
            ? await usuarios.FindByIdAsync(link.UsuarioId.ToString())
            : null;
        return usuario is { Ativo: true } ? (link!, usuario) : throw new RegraDeNegocioException("Entre de novo com a senha.");
    }

    private async Task<IReadOnlyList<string>> Ativar(Usuario usuario, bool porEmail)
    {
        usuario.SegundoFatorPorEmail = porEmail;
        await usuarios.UpdateAsync(usuario);
        await usuarios.SetTwoFactorEnabledAsync(usuario, true);
        Auditar(usuario, AcaoAuditada.SegundoFatorLigado, porEmail ? "e-mail" : "app autenticador");
        await banco.SaveChangesAsync();
        return (await usuarios.GenerateNewTwoFactorRecoveryCodesAsync(usuario, CodigosDeRecuperacao))?.ToList() ?? [];
    }

    private async Task MandarCodigo(Usuario usuario, string chave, CancellationToken ct)
    {
        var codigo = await usuarios.GenerateTwoFactorTokenAsync(usuario, TokenOptions.DefaultEmailProvider);
        emails.Enfileirar(chave, usuario.Email!, $"Código de acesso: {codigo}",
            $"""
            Olá, {usuario.Nome}.

            Seu código de acesso à Avalia Sales: {codigo}

            Vale alguns minutos. Se não foi você, troque a senha.
            """);
        await banco.SaveChangesAsync(ct);
    }

    private async Task<bool> Confere(Usuario usuario, string? codigo)
    {
        var limpo = (codigo ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        if (limpo.Length == 6 && limpo.All(char.IsAsciiDigit))
        {
            return usuario.SegundoFatorPorEmail ? await CodigoDoEmail(usuario, limpo) : await CodigoDoApp(usuario, limpo);
        }

        return limpo.Length > 0 && (await usuarios.RedeemTwoFactorRecoveryCodeAsync(usuario, (codigo ?? string.Empty).Trim())).Succeeded;
    }

    private Task<bool> CodigoDoApp(Usuario usuario, string? codigo) =>
        usuarios.VerifyTwoFactorTokenAsync(usuario, usuarios.Options.Tokens.AuthenticatorTokenProvider, (codigo ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal));

    private Task<bool> CodigoDoEmail(Usuario usuario, string? codigo) =>
        usuarios.VerifyTwoFactorTokenAsync(usuario, TokenOptions.DefaultEmailProvider, (codigo ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal));

    /// <summary>a*****@dominio.com.br: a tela diz para onde foi sem expor o endereço inteiro a quem só tem a senha.</summary>
    private static string Mascarar(string email)
    {
        var arroba = email.IndexOf('@', StringComparison.Ordinal);
        return arroba <= 1 ? email : email[0] + new string('*', Math.Min(arroba - 1, 5)) + email[arroba..];
    }

    /// <summary>A operadora não tem revendedor, e a auditoria é por revendedor: fica só para o dono.</summary>
    private void Auditar(Usuario usuario, AcaoAuditada acao, string? depois = null)
    {
        if (usuario.RevendedorId is not null)
        {
            auditoria.Registrar(acao, $"Login {usuario.Nome}", usuario.Id.ToString(), null, depois);
        }
    }

    private async Task<Usuario> AcharDesligado(Guid id)
    {
        var usuario = await Achar(id);
        return usuario.TwoFactorEnabled ? throw new RegraDeNegocioException("O segundo fator já está ligado.") : usuario;
    }

    private async Task<Usuario> Achar(Guid id)
    {
        var usuario = await usuarios.FindByIdAsync(id.ToString()) ?? throw new RegraDeNegocioException("Login não encontrado.");
        return Pode(usuario) ? usuario : throw new RegraDeNegocioException("O segundo fator é para o revendedor e a operadora.");
    }
}
