using AvaliaSales.Domain.Acesso;
using AvaliaSales.Domain.Pagamentos;
using AvaliaSales.Domain.Placas;
using AvaliaSales.Domain.Vendas;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Vendas;

/// <summary>Uma linha do ranking do mês: posição, primeiro nome e vendas pagas. Valor e comissão dos outros não aparecem.</summary>
public sealed record PosicaoNoRanking(int Posicao, string Nome, int Vendas, bool Voce);

/// <summary>O mês do vendedor: a meta que o revendedor definiu, o que ele já vendeu pago e o ranking da equipe.</summary>
public sealed record ResumoDoVendedor(long? MetaMensalCents, long VendidoNoMesCents, int VendasNoMes, IReadOnlyList<PosicaoNoRanking> Ranking);

/// <summary>Uma placa vendida na carteira: o cliente para chamar, quando vence e quanto é lida.</summary>
public sealed record PlacaDaCarteira(Guid VendaId, string Codigo, Guid ClienteId, string Cliente, string Responsavel, string Telefone,
    EstadoDaPlaca Estado, DateTimeOffset? VenceEm, long TotalLeituras, int LeiturasEm30Dias);

/// <summary>
/// O que o vendedor acompanha além das vendas: a meta, o ranking da equipe e a
/// carteira das placas que ele vendeu. O vendedor vê só a carteira dele; o
/// revendedor vê a da revenda inteira. O filtro do revendedor faz o resto.
/// </summary>
public sealed class PainelDoVendedor(IBancoDeDados banco, IUsuarioAtual usuario, TimeProvider relogio)
{
    private static readonly TimeZoneInfo Brasilia = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    public const int PlacasNaCarteira = 500;

    /// <summary>Venda paga no mês de Brasília conta: venda registrada, sem cortesia, com a cobrança dela paga.</summary>
    public async Task<ResumoDoVendedor> Resumo(CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        var local = TimeZoneInfo.ConvertTime(agora, Brasilia);
        var inicio = new DateTimeOffset(local.Year, local.Month, 1, 0, 0, 0, local.Offset).ToUniversalTime();

        var pagas = await (
            from v in banco.Vendas.AsNoTracking()
            where v.VendidaEm >= inicio && v.Situacao == SituacaoDaVenda.Registrada && !v.Cortesia
                && banco.Cobrancas.Any(c => c.Origem == OrigemDaCobranca.Venda && c.OrigemId == (v.GrupoId ?? v.Id) && c.Situacao == SituacaoDaCobranca.Paga)
            group v by v.VendedorId into g
            select new { VendedorId = g.Key, Vendas = g.Count(), ValorCents = g.Sum(v => v.ValorCents) }).ToListAsync(ct);
        var equipe = await banco.Vendedores.AsNoTracking().Where(v => v.Ativo).Select(v => new { v.Id, v.Nome, v.MetaMensalCents }).ToListAsync(ct);

        var eu = usuario.UsuarioId;
        var ranking = equipe
            .Select(v => new { v.Id, Nome = v.Nome.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0], Vendas = pagas.FirstOrDefault(p => p.VendedorId == v.Id)?.Vendas ?? 0 })
            .OrderByDescending(v => v.Vendas).ThenBy(v => v.Nome, StringComparer.Ordinal)
            .Select((v, i) => new PosicaoNoRanking(i + 1, v.Nome, v.Vendas, v.Id == eu)).ToList();
        var minhas = pagas.FirstOrDefault(p => p.VendedorId == eu);
        return new ResumoDoVendedor(equipe.FirstOrDefault(v => v.Id == eu)?.MetaMensalCents, minhas?.ValorCents ?? 0, minhas?.Vendas ?? 0, ranking);
    }

    /// <summary>As placas vendidas e ainda de pé, da que vence primeiro à que vence por último, com as leituras dos últimos 30 dias.</summary>
    public async Task<IReadOnlyList<PlacaDaCarteira>> Carteira(CancellationToken ct)
    {
        var consulta = banco.Vendas.AsNoTracking().Where(v => v.Situacao == SituacaoDaVenda.Registrada && v.PlacaId != null);
        if (usuario.Papel == Papel.Vendedor)
        {
            consulta = consulta.Where(v => v.VendedorId == usuario.UsuarioId);
        }

        var linhas = await (
            from v in consulta
            join p in banco.Placas on v.PlacaId equals p.Id
            join c in banco.Clientes on v.ClienteId equals c.Id
            where p.Situacao != SituacaoDaPlaca.Baixada && c.AnonimizadoEm == null
            orderby p.VenceEm
            select new { v.Id, p.Codigo, PlacaId = p.Id, p.Situacao, p.VenceEm, p.TotalLeituras, ClienteId = c.Id, c.Nome, c.Responsavel, c.Telefone })
            .Take(PlacasNaCarteira).ToListAsync(ct);

        var agora = relogio.GetUtcNow();
        var desde = DateOnly.FromDateTime(agora.UtcDateTime).AddDays(-Placas.FichaDaPlaca.DiasDeLeitura);
        var ids = linhas.Select(l => l.PlacaId).ToList();
        var leituras = await banco.Leituras.AsNoTracking().Where(l => ids.Contains(l.PlacaId) && l.Dia >= desde)
            .GroupBy(l => l.PlacaId).Select(g => new { PlacaId = g.Key, Total = g.Sum(l => l.Total) }).ToListAsync(ct);

        return linhas.Select(l => new PlacaDaCarteira(l.Id, l.Codigo, l.ClienteId, l.Nome, l.Responsavel, l.Telefone,
            Placa.EstadoDe(l.Situacao, l.VenceEm, agora), l.VenceEm, l.TotalLeituras,
            leituras.FirstOrDefault(x => x.PlacaId == l.PlacaId)?.Total ?? 0)).ToList();
    }
}
