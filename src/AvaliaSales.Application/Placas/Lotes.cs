using AvaliaSales.Domain;
using AvaliaSales.Domain.Placas;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Placas;

public sealed record LoteDto(Guid Id, string Rotulo, int Numero, int Quantidade, DateTimeOffset CriadoEm, string? Revendedor, DateTimeOffset? DespachadoEm, DateTimeOffset? AtivadoEm);

public sealed record PlacaParaImprimir(string Codigo, long Sequencia);

public sealed record ImpressaoDoLote(string Rotulo, IReadOnlyList<PlacaParaImprimir> Placas);

/// <summary>
/// A tiragem de placas pela operadora: número do lote em sequência, códigos
/// sorteados sem repetir nenhum já emitido nem reservado, e a sequência da
/// plataforma contínua entre lotes.
/// </summary>
public sealed class Lotes(IBancoDeDados banco, TimeProvider relogio)
{
    private const int RodadasDeSorteio = 20;

    public async Task<IReadOnlyList<LoteDto>> Listar(CancellationToken ct)
    {
        var lotes = await banco.Lotes.AsNoTracking().OrderByDescending(l => l.Numero).ToListAsync(ct);
        var nomes = await banco.Revendedores.AsNoTracking()
            .Where(r => lotes.Select(l => l.RevendedorId).Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Nome, ct);
        return lotes.Select(l => new LoteDto(l.Id, l.Rotulo, l.Numero, l.Quantidade, l.CriadoEm,
            l.RevendedorId is { } r ? nomes.GetValueOrDefault(r) : null, l.DespachadoEm, l.AtivadoEm)).ToList();
    }

    public async Task<LoteDto> Gerar(int quantidade, CancellationToken ct)
    {
        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        await banco.TravarGeracaoDeLotes(ct);

        var numero = (await banco.Lotes.MaxAsync(l => (int?)l.Numero, ct) ?? 0) + 1;
        var lote = new Lote(numero, quantidade, relogio.GetUtcNow());
        var sequencia = await banco.PlacasEmitidas.MaxAsync(p => (long?)p.Sequencia, ct) ?? 0;

        foreach (var codigo in await SortearLivres(quantidade, ct))
        {
            banco.PlacasEmitidas.Add(new PlacaEmitida(codigo, ++sequencia, lote.Id));
        }

        banco.Lotes.Add(lote);
        await banco.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
        return new LoteDto(lote.Id, lote.Rotulo, lote.Numero, lote.Quantidade, lote.CriadoEm, null, null, null);
    }

    /// <summary>Não guarda imagem: o QR é refeito do código, igual toda vez.</summary>
    public async Task<ImpressaoDoLote> Impressao(Guid loteId, CancellationToken ct)
    {
        var lote = await banco.Lotes.AsNoTracking().SingleOrDefaultAsync(l => l.Id == loteId, ct)
            ?? throw new RegraDeNegocioException("Lote não encontrado.");
        var placas = await banco.PlacasEmitidas.AsNoTracking().Where(p => p.LoteId == loteId)
            .OrderBy(p => p.Sequencia).Select(p => new PlacaParaImprimir(p.Codigo, p.Sequencia)).ToListAsync(ct);
        return new ImpressaoDoLote(lote.Rotulo, placas);
    }

    /// <summary>Códigos que ainda não existem. A chave primária é a última palavra; isto evita a colisão antes dela.</summary>
    private async Task<IReadOnlyList<string>> SortearLivres(int quantidade, CancellationToken ct)
    {
        var escolhidos = new HashSet<string>(StringComparer.Ordinal);
        for (var rodada = 0; rodada < RodadasDeSorteio && escolhidos.Count < quantidade; rodada++)
        {
            var candidatos = new HashSet<string>(StringComparer.Ordinal);
            while (candidatos.Count < quantidade - escolhidos.Count)
            {
                var codigo = CodigoDaPlaca.Sortear();
                if (!escolhidos.Contains(codigo))
                {
                    candidatos.Add(codigo);
                }
            }

            var usados = await banco.PlacasEmitidas.Where(p => candidatos.Contains(p.Codigo)).Select(p => p.Codigo)
                .Concat(banco.CodigosReservados.Where(c => candidatos.Contains(c.Codigo)).Select(c => c.Codigo))
                .ToListAsync(ct);
            candidatos.ExceptWith(usados);
            escolhidos.UnionWith(candidatos);
        }

        return escolhidos.Count == quantidade
            ? escolhidos.ToList()
            : throw new InvalidOperationException("Não sobrou código livre suficiente depois de 20 rodadas.");
    }
}
