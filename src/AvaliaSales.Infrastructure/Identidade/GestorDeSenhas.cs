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
    /// <summary>Intervalo mínimo entre dois e-mails de confirmação; a tela mostra a contagem.</summary>
    public static readonly TimeSpan EsperaParaReenviar = TimeSpan.FromSeconds(60);

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

            A {revenda.Nome} cadastrou você para vender as placas de avaliação.

            Criar minha senha: {endereco}

            O link vale 24 horas.
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

        // Um pedido por minuto por conta, calado: o e-mail anterior ainda vale, e quem digita o endereço
        // de outra pessoa em loop não enche a caixa dela (o limite por IP deixa passar 20 por minuto).
        var limite = relogio.GetUtcNow() - EsperaParaReenviar;
        if (await banco.LinksDeSenha.AnyAsync(l => l.UsuarioId == usuario.Id && l.Finalidade == FinalidadeDoLink.Redefinicao && l.CriadoEm > limite, ct))
        {
            return;
        }

        var (link, endereco) = await Emitir(usuario.Id, FinalidadeDoLink.Redefinicao, ct);
        emails.Enfileirar($"senha:{link.Id:N}", usuario.Email!, "Trocar a senha",
            $"""
            Olá, {usuario.Nome}.

            Recebemos um pedido para trocar a senha da sua conta.

            Criar senha nova: {endereco}

            O link vale 1 hora. Se não foi você que pediu, ignore este e-mail.
            """);
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Na mesma transação do cadastro: o e-mail sai só se o cadastro gravar. Leva
    /// o código, para digitar na tela, e o link, para quem abre o e-mail em outro
    /// aparelho. Um pedido por minuto.
    /// </summary>
    public async Task PedirConfirmacao(Guid usuarioId, CancellationToken ct)
    {
        var usuario = await usuarios.FindByIdAsync(usuarioId.ToString()) ?? throw new RegraDeNegocioException("Login não encontrado.");
        if (usuario.EmailConfirmed)
        {
            throw new RegraDeNegocioException("Este e-mail já está confirmado.");
        }

        var limite = relogio.GetUtcNow() - EsperaParaReenviar;
        if (await banco.LinksDeSenha.AnyAsync(l => l.UsuarioId == usuario.Id && l.Finalidade == FinalidadeDoLink.ConfirmacaoDeEmail && l.CriadoEm > limite, ct))
        {
            throw new RegraDeNegocioException("Aguarde 1 minuto para pedir outro e-mail.");
        }

        var (link, endereco) = await Emitir(usuario.Id, FinalidadeDoLink.ConfirmacaoDeEmail, ct);
        var codigo = link.GerarCodigo();
        emails.Enfileirar($"senha:{link.Id:N}", usuario.Email!, $"Código de confirmação: {codigo}",
            $"""
            Olá, {usuario.Nome}.

            Seu código para confirmar o e-mail na Avalia Sales: {codigo}

            Ou confirme pelo botão, se abriu o e-mail em outro aparelho.

            Confirmar e-mail: {endereco}

            Vale 2 dias. Se não foi você, ignore este e-mail.
            """);
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>O código do último e-mail de confirmação, digitado por quem está logado.</summary>
    public async Task ConfirmarComCodigo(Guid usuarioId, string? codigo, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        var link = await banco.LinksDeSenha
            .FromSql($"select * from links_de_senha where usuario_id = {usuarioId} and finalidade = 'ConfirmacaoDeEmail' and encerrado_em is null order by criado_em desc limit 1 for update")
            .SingleOrDefaultAsync(ct);
        var usuario = await usuarios.FindByIdAsync(usuarioId.ToString()) ?? throw new RegraDeNegocioException("Login não encontrado.");
        if (usuario.EmailConfirmed)
        {
            return;
        }

        if (link is null || !link.Valido(agora))
        {
            throw new RegraDeNegocioException("Código vencido. Peça outro e-mail.");
        }

        if (!link.ConfereCodigo(codigo, agora))
        {
            await banco.SaveChangesAsync(ct);
            await transacao.CommitAsync(ct);
            throw new RegraDeNegocioException(link.Valido(agora) ? "Código não confere." : "Código errado 5 vezes. Peça outro e-mail.");
        }

        usuario.EmailConfirmed = true;
        await usuarios.UpdateAsync(usuario);
        link.Encerrar(agora);
        await banco.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
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
    /// Troca logada: confere a senha atual (erros seguidos bloqueiam como na
    /// entrada), grava a nova, encerra todas as sessões e avisa por e-mail. Quem
    /// chama abre a sessão nova deste aparelho.
    /// </summary>
    public async Task<Usuario> Trocar(Guid usuarioId, string? atual, string? nova, CancellationToken ct)
    {
        var usuario = await usuarios.FindByIdAsync(usuarioId.ToString()) ?? throw new RegraDeNegocioException("Login não encontrado.");
        if (await usuarios.IsLockedOutAsync(usuario))
        {
            throw new RegraDeNegocioException("Muitas tentativas erradas. Espere 15 minutos.");
        }

        if (!await usuarios.CheckPasswordAsync(usuario, atual ?? string.Empty))
        {
            await usuarios.AccessFailedAsync(usuario);
            throw new RegraDeNegocioException("Senha atual não confere.");
        }

        if (nova == atual)
        {
            throw new RegraDeNegocioException("A senha nova precisa ser diferente da atual.");
        }

        foreach (var validador in usuarios.PasswordValidators)
        {
            var conferida = await validador.ValidateAsync(usuarios, usuario, nova ?? string.Empty);
            if (!conferida.Succeeded)
            {
                throw new RegraDeNegocioException(string.Join(" ", conferida.Errors.Select(Contas.Traduzir)));
            }
        }

        var agora = relogio.GetUtcNow();
        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        usuario.PasswordHash = usuarios.PasswordHasher.HashPassword(usuario, nova!);
        usuario.AccessFailedCount = 0;
        usuario.LockoutEnd = null;
        usuario.SecurityStamp = Guid.NewGuid().ToString("N");
        await usuarios.UpdateAsync(usuario);
        await banco.Sessoes.Where(s => s.UsuarioId == usuario.Id && s.EncerradaEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EncerradaEm, agora), ct);
        emails.Enfileirar($"senha-trocada:{usuario.Id:N}:{agora.ToUnixTimeSeconds()}", usuario.Email!, "Sua senha foi alterada",
            $"""
            Olá, {usuario.Nome}.

            A senha da sua conta na Avalia Sales foi alterada agora. Os outros aparelhos saíram da conta.

            Se não foi você, use "Esqueci a senha" em {plataforma.Value.UrlDoPainel}/esqueci e fale com o suporte.
            """);
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
        var daFinalidade = link is not null && (finalidade is { } f ? link.Finalidade == f : link.Finalidade is FinalidadeDoLink.Convite or FinalidadeDoLink.Redefinicao);
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
