using AvaliaSales.Infrastructure.Banco;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Infrastructure.Identidade;

/// <summary>
/// Refresh token rotativo (OAuth 2.0 Security BCP, RFC 9700, seção 4.14):
/// cada renovação entrega um token novo e encerra o anterior; o anterior
/// apresentado de novo encerra o login inteiro, porque alguém o copiou.
/// </summary>
public sealed class GestorDeSessoes(BancoDeDados banco, TimeProvider relogio)
{
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

        if (sessao.EncerradaEm is not null)
        {
            await EncerrarFamilia(sessao.Familia, agora, ct);
            await transacao.CommitAsync(ct);
            return null;
        }

        var usuario = await banco.Users.SingleOrDefaultAsync(u => u.Id == sessao.UsuarioId, ct);
        if (!sessao.Valida(agora) || usuario is not { Ativo: true })
        {
            return null;
        }

        sessao.Encerrar(agora);
        var (nova, novoToken) = Sessao.Abrir(usuario.Id, sessao.Familia, agora);
        banco.Sessoes.Add(nova);
        await banco.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
        return (usuario, novoToken);
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
