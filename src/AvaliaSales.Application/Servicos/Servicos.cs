using AvaliaSales.Domain;
using AvaliaSales.Domain.Servicos;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Servicos;

public sealed record OrdemDto(Guid Id, long Numero, string Revendedor, string Produto, string Cliente, SituacaoDaOrdem Situacao, string? Observacao,
    DateTimeOffset CriadaEm, DateTimeOffset? IniciadaEm, DateTimeOffset? EntregueEm);

public sealed record AnotarOrdem(string? Observacao);

/// <summary>
/// As ordens de serviço: a operadora executa e anda com elas; o revendedor
/// acompanha as dele. Cada passo avisa o dono no sino.
/// </summary>
public sealed class Servicos(IBancoDeDados banco, IRevendedorAtual atual, Notificacoes.Notificacoes notificacoes, TimeProvider relogio)
{
    public async Task<IReadOnlyList<OrdemDto>> Listar(CancellationToken ct)
    {
        var ordens = await banco.OrdensDeServico.AsNoTracking().ToListAsync(ct);
        var ids = ordens.Select(o => o.RevendedorId).Distinct().ToList();
        var nomes = await banco.Revendedores.AsNoTracking().Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Nome, ct);
        // A fazer e em andamento primeiro, do mais antigo: é a fila. O encerrado vem depois, do mais recente.
        return ordens
            .OrderBy(o => o.Situacao is SituacaoDaOrdem.AFazer or SituacaoDaOrdem.EmAndamento ? 0 : 1)
            .ThenBy(o => o.Situacao is SituacaoDaOrdem.AFazer or SituacaoDaOrdem.EmAndamento ? o.CriadaEm.UtcTicks : -(o.EntregueEm ?? o.CriadaEm).UtcTicks)
            .Select(o => new OrdemDto(o.Id, o.Numero, nomes.GetValueOrDefault(o.RevendedorId, ""), o.Produto, o.Cliente, o.Situacao, o.Observacao,
                o.CriadaEm, o.IniciadaEm, o.EntregueEm))
            .ToList();
    }

    public Task Iniciar(Guid id, CancellationToken ct) => Mudar(id, (o, agora) => o.Iniciar(agora), "Serviço em andamento", "A Avalia começou", ct);

    public Task Entregar(Guid id, CancellationToken ct) => Mudar(id, (o, agora) => o.Entregar(agora), "Serviço entregue", "A Avalia entregou", ct);

    public Task Cancelar(Guid id, CancellationToken ct) => Mudar(id, (o, _) => o.Cancelar(), "Serviço cancelado", "A Avalia cancelou", ct);

    public async Task Anotar(Guid id, string? observacao, CancellationToken ct)
    {
        (await Achar(id, ct)).Anotar(observacao);
        await banco.SaveChangesAsync(ct);
    }

    private async Task Mudar(Guid id, Action<OrdemDeServico, DateTimeOffset> mudar, string titulo, string acao, CancellationToken ct)
    {
        if (!atual.Operadora)
        {
            throw new RegraDeNegocioException("Só a Avalia anda com a ordem de serviço.");
        }

        var ordem = await Achar(id, ct);
        mudar(ordem, relogio.GetUtcNow());
        notificacoes.ParaDono(ordem.RevendedorId, titulo, $"{acao} {ordem.Produto} de {ordem.Cliente} (ordem {ordem.Numero}).", "/painel/servicos");
        banco.AvisarAoVivo("servico", ordem.RevendedorId, operadora: true);
        await banco.SaveChangesAsync(ct);
    }

    private async Task<OrdemDeServico> Achar(Guid id, CancellationToken ct) =>
        await banco.OrdensDeServico.SingleOrDefaultAsync(o => o.Id == id, ct) ?? throw new RegraDeNegocioException("Ordem de serviço não encontrada.");
}
