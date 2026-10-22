using AvaliaSales.Domain.Notificacoes;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Notificacoes;

public sealed record NotificacaoDto(Guid Id, string Titulo, string Texto, string? Link, DateTimeOffset CriadaEm, bool Lida, string? Revendedor);

public sealed record CaixaDeNotificacoes(IReadOnlyList<NotificacaoDto> Notificacoes, int NaoLidas);

/// <summary>
/// O sino do cabeçalho. Quem grava chama ParaDono ou ParaOperadora na
/// transação dela (o SaveChanges é de quem chama) e o aviso ao vivo refaz o
/// sino aberto. O dono lê as da revenda dele; a operadora, as que são para ela.
/// </summary>
public sealed class Notificacoes(IBancoDeDados banco, IRevendedorAtual atual, TimeProvider relogio)
{
    public const int NaCaixa = 30;

    public void ParaDono(Guid revendedor, string titulo, string texto, string? link)
    {
        banco.Notificacoes.Add(new Notificacao(false, titulo, texto, link, relogio.GetUtcNow(), revendedor));
        banco.AvisarAoVivo("notificacao", revendedor);
    }

    public void ParaOperadora(Guid revendedor, string titulo, string texto, string? link)
    {
        banco.Notificacoes.Add(new Notificacao(true, titulo, texto, link, relogio.GetUtcNow(), revendedor));
        banco.AvisarAoVivo("notificacao", revendedor, operadora: true);
    }

    public async Task<CaixaDeNotificacoes> Caixa(CancellationToken ct)
    {
        var minhas = Minhas();
        var naoLidas = await minhas.CountAsync(n => n.LidaEm == null, ct);
        var lista = await minhas.OrderByDescending(n => n.CriadaEm).Take(NaCaixa).ToListAsync(ct);
        var nomes = atual.Operadora
            ? await banco.Revendedores.AsNoTracking().Where(r => lista.Select(n => n.RevendedorId).Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Nome, ct)
            : [];
        return new CaixaDeNotificacoes(
            lista.Select(n => new NotificacaoDto(n.Id, n.Titulo, n.Texto, n.Link, n.CriadaEm, n.LidaEm != null, nomes.GetValueOrDefault(n.RevendedorId))).ToList(),
            naoLidas);
    }

    public async Task MarcarLidas(Guid? id, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        var alvo = Minhas().Where(n => n.LidaEm == null);
        if (id is { } uma)
        {
            alvo = alvo.Where(n => n.Id == uma);
        }

        await alvo.ExecuteUpdateAsync(s => s.SetProperty(n => n.LidaEm, agora), ct);
    }

    /// <summary>O filtro do banco já limita o dono à revenda dele; aqui separa o que é para quem.</summary>
    private IQueryable<Notificacao> Minhas()
    {
        var paraOperadora = atual.Operadora;
        return banco.Notificacoes.Where(n => n.ParaOperadora == paraOperadora);
    }
}
