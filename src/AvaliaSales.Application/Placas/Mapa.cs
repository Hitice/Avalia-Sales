using AvaliaSales.Domain.Placas;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Placas;

public sealed record LugarNoMapa(Guid ClienteId, string Nome, string LugarNoGoogle, int Placas, int PlacasNoAr, long Leituras30Dias);

/// <summary>
/// Os lugares vendidos para o mapa. Só o id do lugar no Google sai daqui: a
/// posição é lida pelo próprio mapa do Google no navegador, porque dado do
/// Places só pode aparecer em mapa do Google e não é guardado.
/// </summary>
public sealed class Mapa(IBancoDeDados banco, TimeProvider relogio)
{
    public async Task<IReadOnlyList<LugarNoMapa>> Lugares(CancellationToken ct)
    {
        var desde = DateOnly.FromDateTime(relogio.GetUtcNow().UtcDateTime).AddDays(-30);
        var linhas = await (
            from c in banco.Clientes.AsNoTracking().Where(c => c.LugarNoGoogle != null)
            join v in banco.Vendas on c.Id equals v.ClienteId
            join p in banco.Placas on v.PlacaId equals p.Id
            select new
            {
                c.Id,
                c.Nome,
                Lugar = c.LugarNoGoogle!,
                NoAr = p.Situacao == SituacaoDaPlaca.Ativa,
                Leituras = banco.Leituras.Where(l => l.PlacaId == p.Id && l.Dia >= desde).Sum(l => (long?)l.Total) ?? 0,
            })
            .ToListAsync(ct);

        return linhas.GroupBy(l => new { l.Id, l.Nome, l.Lugar })
            .Select(g => new LugarNoMapa(g.Key.Id, g.Key.Nome, g.Key.Lugar, g.Count(), g.Count(x => x.NoAr), g.Sum(x => x.Leituras)))
            .OrderByDescending(l => l.Leituras30Dias)
            .ToList();
    }
}
