using AvaliaSales.Infrastructure.Banco;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Infrastructure.Identidade;

/// <summary>
/// Refresh token rotativo (OAuth 2.0 Security BCP, RFC 9700, seção 4.14):
/// cada renovação entrega um token novo e encerra o anterior; o anterior
/// apresentado de novo encerra o login inteiro, porque alguém o copiou.
/// Exceção: o anterior reapresentado até <see cref="JanelaDeTolerancia"/> depois
/// da troca é duas abas, ou uma retentativa depois de resposta perdida, e não
/// roubo. Ele recebe uma sessão nova da mesma família e a que já existia
/// continua valendo: o navegador fica com o último cookie que processar, e
/// qualquer um dos dois renova.
/// </summary>
public sealed class GestorDeSessoes(BancoDeDados banco, TimeProvider relogio)
{
    public static readonly TimeSpan JanelaDeTolerancia = TimeSpan.FromSeconds(10);

    /// <summary>Quantas sessões extras um mesmo token trocado pode gerar dentro da janela.</summary>
    public const int ReenviosToleradosPorToken = 10;

    public async Task<string> Abrir(Guid usuarioId, CancellationToken ct)
    {
        var (sessao, token) = Sessao.Abrir(usuarioId, null, relogio.GetUtcNow());
        banco.Sessoes.Add(sessao);
        await banco.SaveChangesAsync(ct);
        return token;
    }

    /// <summary>O usuário e o token novo, ou nulo quando a sessão não vale mais.</summary>
    public async Task<(Usuario Usuario, string Token)?> Renovar(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var agora = relogio.GetUtcNow();
        var hash = Sessao.Hash(token);
        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        var sessao = await banco.Sessoes.FromSql($"select * from sessoes where hash_do_token = {hash} for update").SingleOrDefaultAsync(ct);
        if (sessao is null)
        {
            return null;
        }

        var usuario = await banco.Users.SingleOrDefaultAsync(u => u.Id == sessao.UsuarioId, ct);
        var reenvio = false;
        if (sessao.EncerradaEm is { } encerrada)
        {
            if (usuario is not { Ativo: true } || !await ReenvioTolerado(sessao, encerrada, agora, ct))
            {
                await EncerrarFamilia(sessao.Familia, agora, ct);
                await transacao.CommitAsync(ct);
                return null;
            }

            reenvio = true;
        }

        if (usuario is not { Ativo: true } || (!reenvio && !sessao.Valida(agora)))
        {
            return null;
        }

        if (!reenvio)
        {
            sessao.Encerrar(agora);
        }

        var (nova, novoToken) = Sessao.Abrir(usuario.Id, sessao.Familia, agora);
        banco.Sessoes.Add(nova);
        await banco.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
        return (usuario, novoToken);
    }

    /// <summary>
    /// Troca recente e a família ainda viva: foi rotação, e não saída nem
    /// revogação (essas encerram a família toda). O limite de reenvios impede
    /// que um token copiado gere sessões sem fim dentro da janela.
    /// </summary>
    private async Task<bool> ReenvioTolerado(Sessao sessao, DateTimeOffset encerrada, DateTimeOffset agora, CancellationToken ct)
    {
        if (agora - encerrada > JanelaDeTolerancia || agora >= sessao.ExpiraEm)
        {
            return false;
        }

        var familia = sessao.Familia;
        var viva = await banco.Sessoes.AsNoTracking().AnyAsync(s => s.Familia == familia && s.EncerradaEm == null && s.ExpiraEm > agora, ct);
        var sucessoras = await banco.Sessoes.AsNoTracking().CountAsync(s => s.Familia == familia && s.Id != sessao.Id && s.CriadaEm >= encerrada, ct);
        return viva && sucessoras <= ReenviosToleradosPorToken;
    }

    public async Task Encerrar(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        var hash = Sessao.Hash(token);
        var familia = await banco.Sessoes.Where(s => s.HashDoToken == hash).Select(s => (Guid?)s.Familia).SingleOrDefaultAsync(ct);
        if (familia is { } f)
        {
            await EncerrarFamilia(f, relogio.GetUtcNow(), ct);
        }
    }

    private Task<int> EncerrarFamilia(Guid familia, DateTimeOffset agora, CancellationToken ct) =>
        banco.Sessoes.Where(s => s.Familia == familia && s.EncerradaEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EncerradaEm, agora), ct);
}
