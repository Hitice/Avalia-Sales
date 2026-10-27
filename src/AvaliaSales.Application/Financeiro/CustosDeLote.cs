using AvaliaSales.Domain;
using AvaliaSales.Domain.Financeiro;
using AvaliaSales.Domain.Placas;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Financeiro;

/// <summary>
/// Um lote e o custo real de produção dele. Sem custo informado, o lote despachado a um pedido é
/// pendência: o resultado do grupo não tem como levar o custo das placas vendidas.
/// </summary>
public sealed record CustoDoLoteDto(
    Guid LoteId, string Rotulo, string? Titulo, int Quantidade, string? Revendedor, DateTimeOffset? AtivadoEm,
    bool Informado, bool Pendente, long GraficaCents, long AcrilicoCents, long TotalCents, long PorPlacaCents,
    Guid? FornecedorId = null, string? Fornecedor = null);

public sealed record GravarCustoDoLote(long GraficaCents, long AcrilicoCents, Guid? FornecedorId = null);

public sealed record NovoCustoDoLote(Guid LoteId, long GraficaCents, long AcrilicoCents, Guid? FornecedorId = null);

/// <summary>
/// O custo de produção por lote, com criar, editar e excluir. O custo por placa é o total dividido
/// pela quantidade do lote. É cadastro da operadora, não lançamento: a tabela é fechada às revendas.
/// O resultado do grupo lê o custo vigente do lote do mês da ativação em diante, então o custo de lote
/// ativado em mês fechado da operadora, ou antes de um, não muda até a reabertura (ADR 0025).
/// </summary>
public sealed class CustosDeLote(IBancoDeDados banco, Fornecedores fornecedores, TimeProvider relogio)
{
    /// <summary>
    /// Os lotes da operadora, do número mais alto ao mais baixo, sem os importados de outro sistema (que
    /// nasceram ativados e não têm produção a custear). Com <paramref name="soPendentes"/>, os despachados
    /// sem custo. A busca vale para o número, o título e a revenda.
    /// </summary>
    public async Task<Pagina<CustoDoLoteDto>> Listar(bool soPendentes, string? busca, PedidoDePagina pedido, CancellationToken ct)
    {
        var consulta =
            from l in banco.Lotes.AsNoTracking()
            where (l.PedidoId != null || l.DespachadoEm == null) && l.ArquivadoEm == null
            join c in banco.CustosDeLote.AsNoTracking() on l.Id equals c.LoteId into custos
            from c in custos.DefaultIfEmpty()
            select new { Lote = l, Custo = c };
        if (soPendentes)
        {
            consulta = consulta.Where(x => x.Lote.PedidoId != null && x.Custo == null);
        }

        var termo = Paginacao.Termo(busca);
        if (termo.Length > 0)
        {
            int? numero = int.TryParse(new string(termo.Where(char.IsAsciiDigit).ToArray()), out var lido) ? lido : null;
            consulta = consulta.Where(x => (x.Lote.Titulo != null && Busca.Normalizar(x.Lote.Titulo).Contains(Busca.Normalizar(termo)))
                || (numero != null && x.Lote.Numero == numero)
                || banco.Revendedores.Any(r => r.Id == x.Lote.RevendedorId && Busca.Normalizar(r.Nome).Contains(Busca.Normalizar(termo))));
        }

        var pagina = await consulta.OrderByDescending(x => x.Lote.Numero).ThenByDescending(x => x.Lote.Id)
            .Select(x => new { x.Lote, x.Custo }).PaginarAsync(pedido, ct);
        var ids = pagina.Itens.Select(x => x.Lote.RevendedorId).Where(r => r != null).Select(r => r!.Value).Distinct().ToList();
        var nomes = await banco.Revendedores.AsNoTracking().Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Nome, ct);
        var fornecedoresDaPagina = await fornecedores.Nomes(pagina.Itens.Where(x => x.Custo?.FornecedorId != null).Select(x => x.Custo!.FornecedorId!.Value), ct);
        return pagina.Mapear(x => Dto(x.Lote, x.Custo, x.Lote.RevendedorId is { } r ? nomes.GetValueOrDefault(r) : null, x.Custo?.FornecedorId is { } f ? fornecedoresDaPagina.GetValueOrDefault(f) : null));
    }

    public async Task<CustoDoLoteDto> Criar(NovoCustoDoLote pedido, CancellationToken ct)
    {
        var lote = await AcharLote(pedido.LoteId, ct);
        if (await banco.CustosDeLote.AnyAsync(c => c.LoteId == lote.Id, ct))
        {
            throw new RegraDeNegocioException($"Lote {lote.Rotulo} já com custo. Edite o custo.");
        }

        await ExigirSemMesFechado(lote, ct);
        var custo = new CustoDoLote(lote.Id, lote.Quantidade, pedido.GraficaCents, pedido.AcrilicoCents, relogio.GetUtcNow());
        await fornecedores.ExigirAtivo(pedido.FornecedorId, ct);
        custo.DefinirFornecedor(pedido.FornecedorId);
        banco.CustosDeLote.Add(custo);
        await banco.SaveChangesAsync(ct);
        return Dto(lote, custo, null);
    }

    public async Task<CustoDoLoteDto> Alterar(Guid loteId, GravarCustoDoLote pedido, CancellationToken ct)
    {
        var lote = await AcharLote(loteId, ct);
        var custo = await banco.CustosDeLote.SingleOrDefaultAsync(c => c.LoteId == loteId, ct)
            ?? throw new RegraDeNegocioException($"Lote {lote.Rotulo} sem custo. Informe o custo.");
        await ExigirSemMesFechado(lote, ct);
        custo.Alterar(lote.Quantidade, pedido.GraficaCents, pedido.AcrilicoCents, relogio.GetUtcNow());
        if (custo.FornecedorId != pedido.FornecedorId)
        {
            await fornecedores.ExigirAtivo(pedido.FornecedorId, ct);
            custo.DefinirFornecedor(pedido.FornecedorId);
        }

        await banco.SaveChangesAsync(ct);
        return Dto(lote, custo, null);
    }

    public async Task Excluir(Guid loteId, CancellationToken ct)
    {
        var custo = await banco.CustosDeLote.SingleOrDefaultAsync(c => c.LoteId == loteId, ct)
            ?? throw new RegraDeNegocioException("Custo não encontrado.");
        await ExigirSemMesFechado(await AcharLote(loteId, ct), ct);
        banco.CustosDeLote.Remove(custo);
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>
    /// O custo do lote entra no mês da ativação (coluna da operadora) e no das placas vendidas ou perdidas, sempre
    /// da ativação em diante: com um mês fechado nesse intervalo, mudar o custo mudaria um resultado fechado.
    /// </summary>
    private async Task ExigirSemMesFechado(Lote lote, CancellationToken ct)
    {
        if (lote.AtivadoEm is not { } ativadoEm)
        {
            return;
        }

        var desde = Lancamento.CompetenciaDe(ativadoEm);
        var fechados = await banco.FechamentosDaOperadora.AsNoTracking().Where(f => f.ReabertoEm == null).Select(f => f.Competencia).ToListAsync(ct);
        var fechado = fechados.Where(f => string.CompareOrdinal(f, desde) >= 0).Order(StringComparer.Ordinal).FirstOrDefault();
        if (fechado is not null)
        {
            throw new RegraDeNegocioException($"Lote {lote.Rotulo} em mês fechado ({Fechamentos.Nome(fechado)}). Reabra o mês.");
        }
    }

    private async Task<Lote> AcharLote(Guid loteId, CancellationToken ct)
    {
        var lote = await banco.Lotes.AsNoTracking().SingleOrDefaultAsync(l => l.Id == loteId, ct) ?? throw new RegraDeNegocioException("Lote não encontrado.");
        return lote.PedidoId is null && lote.DespachadoEm is not null ? throw new RegraDeNegocioException("Lote importado sem custo de produção.") : lote;
    }

    private static CustoDoLoteDto Dto(Lote l, CustoDoLote? c, string? revendedor, string? fornecedor = null) =>
        new(l.Id, l.Rotulo, l.Titulo, l.Quantidade, revendedor, l.AtivadoEm, c is not null, c is null && l.PedidoId is not null,
            c?.GraficaCents ?? 0, c?.AcrilicoCents ?? 0, c?.TotalCents ?? 0, c?.PorPlacaCents ?? 0, c?.FornecedorId, fornecedor);
}
