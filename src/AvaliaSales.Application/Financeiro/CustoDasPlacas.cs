using AvaliaSales.Domain.Financeiro;
using AvaliaSales.Domain.Placas;
using AvaliaSales.Domain.Vendas;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Financeiro;

/// <summary>
/// As linhas do razão do custo da placa que sai sem receita. A perdida e a
/// quebrada (em estoque, na troca ou na venda cancelada que não voltou) são
/// perda pelo custo dela; a dada em cortesia é custo da placa. Defeito de
/// impressão não é perda: o acrílico volta com outro QR. O custo da placa
/// vendida e paga não passa por aqui: a Dre tira da receita da venda.
/// O custo é o da placa, congelado na ativação do lote: o valor pago no
/// pedido de placas, com o cupom, dividido pela quantidade.
/// </summary>
public sealed class CustoDasPlacas(IBancoDeDados banco, Fechamentos fechamentos)
{
    /// <summary>Depois da baixa da placa: lança a perda se o motivo for perdida ou quebrada.</summary>
    public void Baixada(Placa placa, DateTimeOffset agora)
    {
        if (placa is not { Situacao: SituacaoDaPlaca.Baixada, MotivoDaBaixa: MotivoDaBaixa.Perdida or MotivoDaBaixa.Quebrada, CustoCents: > 0 })
        {
            return;
        }

        var como = placa.MotivoDaBaixa == MotivoDaBaixa.Perdida ? "perdida" : "quebrada";
        banco.Lancamentos.Add(new Lancamento(TipoDeLancamento.Perda, Guid.CreateVersion7(), -placa.CustoCents, $"Placa {placa.Codigo} {como}", agora, placaId: placa.Id));
    }

    /// <summary>Baixa desfeita: a perda é estornada, no mês dela se ainda aberto.</summary>
    public async Task BaixaDesfeita(Placa placa, DateTimeOffset agora, CancellationToken ct)
    {
        foreach (var linha in await EmVigor(TipoDeLancamento.Perda, l => l.PlacaId == placa.Id, ct))
        {
            banco.Lancamentos.Add(linha.Estorno("baixa desfeita", agora, await fechamentos.DestinoDaCorrecao(linha.Competencia, ct)));
        }
    }

    /// <summary>A placa dada em cortesia: o custo dela entra no mês.</summary>
    public void Cortesia(Venda venda, DateTimeOffset agora)
    {
        if (venda is { PlacaId: { } placaId, CustoCents: > 0 })
        {
            banco.Lancamentos.Add(new Lancamento(TipoDeLancamento.CustoDaPlaca, venda.Id, -venda.CustoCents, "Placa dada em cortesia", agora, venda.Id, placaId: placaId));
        }
    }

    /// <summary>Cortesia cancelada: o custo sai; se a placa não voltou, a baixa dela lança a perda.</summary>
    public async Task VendaCancelada(Venda venda, DateTimeOffset agora, CancellationToken ct)
    {
        foreach (var linha in await EmVigor(TipoDeLancamento.CustoDaPlaca, l => l.OrigemId == venda.Id, ct))
        {
            banco.Lancamentos.Add(linha.Estorno("cortesia cancelada", agora, await fechamentos.DestinoDaCorrecao(linha.Competencia, ct)));
        }
    }

    private async Task<List<Lancamento>> EmVigor(TipoDeLancamento tipo, System.Linq.Expressions.Expression<Func<Lancamento, bool>> filtro, CancellationToken ct)
    {
        var estornadas = banco.Lancamentos.Where(e => e.EstornaId != null).Select(e => e.EstornaId);
        return await banco.Lancamentos.Where(l => l.Tipo == tipo && l.EstornaId == null && !estornadas.Contains(l.Id)).Where(filtro).ToListAsync(ct);
    }
}
