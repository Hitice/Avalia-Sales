using AvaliaSales.Application;
using AvaliaSales.Application.Avisos;
using AvaliaSales.Domain;
using AvaliaSales.Infrastructure.Banco;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Infrastructure.Identidade;

/// <summary>
/// Convite do vendedor, "esqueci a senha" e confirmação do e-mail do dono: um
/// link por e-mail que vale uma vez. O token vai depois do # do endereço, que o navegador não manda ao
/// servidor: não fica em log de Nginx nem em Referer.
/// </summary>
public sealed class GestorDeSenhas(BancoDeDados banco, UserManager<Usuario> usuarios, FilaDeEmails emails,
    IOptions<OpcoesDaPlataforma> plataforma, TimeProvider relogio)
{
    /// <summary>Só para quem ainda não criou a senha; depois disso, o vendedor usa o "esqueci a senha".</summary>
    public async Task<string> Convidar(Guid usuarioId, RemetenteDoEmail revenda, CancellationToken ct)
    {
        var usuario = await usuarios.FindByIdAsync(usuarioId.ToString()) ?? throw new RegraDeNegocioException("Login não encontrado.");
        if (usuario.PasswordHash is not null)
        {
            throw new RegraDeNegocioException("O vendedor já criou a senha.");
        }

        var (link, endereco) = await Emitir(usuario.Id, FinalidadeDoLink.Convite, ct);
        emails.Enfileirar($"senha:{link.Id:N}", usuario.Email!, $"Seu acesso à {revenda.Nome}",
            $"""
            Olá, {usuario.Nome}.

            A {revenda.Nome} cadastrou você para vender as placas de avaliação. Crie sua senha:
            {endereco}

            O link vale 7 dias.
            """, revenda);
        await banco.SaveChangesAsync(ct);
        return endereco;
    }

    /// <summary>Não diz se o e-mail tem conta: a resposta da rota é a mesma.</summary>
    public async Task PedirRedefinicao(string? email, CancellationToken ct)
    {
        var normalizado = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
        var usuario = normalizado is null ? null : await usuarios.FindByEmailAsync(normalizado);
        if (usuario is not { Ativo: true })
        {
            return;
        }

        var (link, endereco) = await Emitir(usuario.Id, FinalidadeDoLink.Redefinicao, ct);
        emails.Enfileirar($"senha:{link.Id:N}", usuario.Email!, "Trocar a senha",
            $"""
            Olá, {usuario.Nome}.

            Para criar uma senha nova:
            {endereco}

            O link vale 1 hora. Se não foi você que pediu, ignore este e-mail.
            """);
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>Na mesma transação do cadastro: o e-mail sai só se o cadastro gravar.</summary>
    public async Task PedirConfirmacao(Guid usuarioId, CancellationToken ct)
    {
        var usuario = await usuarios.FindByIdAsync(usuarioId.ToString()) ?? throw new RegraDeNegocioException("Login não encontrado.");
        if (usuario.EmailConfirmed)
        {
            throw new RegraDeNegocioException("Este e-mail já está confirmado.");
        }

        var (link, endereco) = await Emitir(usuario.Id, FinalidadeDoLink.ConfirmacaoDeEmail, ct);
        emails.Enfileirar($"senha:{link.Id:N}", usuario.Email!, "Confirme seu e-mail",
            $"""
            Olá, {usuario.Nome}.

            Confirme que este e-mail é seu para receber o contrato de revenda e os avisos dos pedidos:
            {endereco}

            O link vale 2 dias.
            """);
        await banco.SaveChangesAsync(ct);
    }

    public async Task Confirmar(string? token, CancellationToken ct)
    {
        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        var (link, usuario) = await Usar(token, FinalidadeDoLink.ConfirmacaoDeEmail, ct);
        usuario.EmailConfirmed = true;
        await usuarios.UpdateAsync(usuario);
        link.Encerrar(relogio.GetUtcNow());
        await banco.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
    }

    /// <summary>Grava a senha, destrava o login e encerra as sessões abertas, que podem ser de quem tinha a senha antiga.</summary>
    public async Task<Usuario> Definir(string? token, string? senha, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        var (link, usuario) = await Usar(token, null, ct);

        foreach (var validador in usuarios.PasswordValidators)
        {
            var conferida = await validador.ValidateAsync(usuarios, usuario, senha ?? string.Empty);
            if (!conferida.Succeeded)
            {
                throw new RegraDeNegocioException(string.Join(" ", conferida.Errors.Select(Contas.Traduzir)));
            }
        }

        usuario.PasswordHash = usuarios.PasswordHasher.HashPassword(usuario, senha!);
        usuario.EmailConfirmed = true;
        usuario.AccessFailedCount = 0;
        usuario.LockoutEnd = null;
        usuario.SecurityStamp = Guid.NewGuid().ToString("N");
        await usuarios.UpdateAsync(usuario);
        link.Encerrar(agora);
        await banco.Sessoes.Where(s => s.UsuarioId == usuario.Id && s.EncerradaEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EncerradaEm, agora), ct);
        await banco.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
        return usuario;
    }

    /// <summary>
    /// O link travado na transação de quem chama. Sem <paramref name="finalidade"/>,
    /// aceita os de senha (convite e redefinição) e recusa o de confirmação.
    /// </summary>
    private async Task<(LinkDeSenha Link, Usuario Usuario)> Usar(string? token, FinalidadeDoLink? finalidade, CancellationToken ct)
    {
        var hash = Sessao.Hash(token ?? string.Empty);
        var link = await banco.LinksDeSenha.FromSql($"select * from links_de_senha where hash_do_token = {hash} for update").SingleOrDefaultAsync(ct);
        var usuario = link is null ? null : await usuarios.FindByIdAsync(link.UsuarioId.ToString());
        var daFinalidade = link is not null && (finalidade is { } f ? link.Finalidade == f : link.Finalidade != FinalidadeDoLink.ConfirmacaoDeEmail);
        return link is not null && daFinalidade && link.Valido(relogio.GetUtcNow()) && usuario is { Ativo: true }
            ? (link, usuario)
            : throw new RegraDeNegocioException("Link vencido ou já usado. Peça outro.");
    }

    private async Task<(LinkDeSenha Link, string Endereco)> Emitir(Guid usuarioId, FinalidadeDoLink finalidade, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        await banco.LinksDeSenha.Where(l => l.UsuarioId == usuarioId && l.Finalidade == finalidade && l.EncerradoEm == null)
            .ExecuteUpdateAsync(l => l.SetProperty(x => x.EncerradoEm, agora), ct);
        var (link, token) = LinkDeSenha.Emitir(usuarioId, finalidade, agora);
        banco.LinksDeSenha.Add(link);
        var tela = finalidade switch
        {
            FinalidadeDoLink.Convite => "convite",
            FinalidadeDoLink.ConfirmacaoDeEmail => "confirmar-email",
            _ => "senha",
        };
        return (link, $"{plataforma.Value.UrlDoPainel.TrimEnd('/')}/{tela}#{token}");
    }
}
