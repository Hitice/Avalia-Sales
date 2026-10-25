using AvaliaSales.Domain;
using AvaliaSales.Domain.Auditoria;
using AvaliaSales.Domain.Financeiro;
using AvaliaSales.Domain.Pedidos;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Financeiro;

/// <summary>Sem saldo inicial, <see cref="Inicio"/> é nulo e o resto é zero: o caixa ainda não é contado.</summary>
public sealed record CaixaDto(string? Inicio, long SaldoInicialCents, long EntradasCents, long SaidasCents, long SaldoCents);

public sealed record NovoSaldoInicial(string Competencia, long ValorCents);

/// <summary>
/// O caixa da revenda: o saldo do primeiro dia de um mês, informado pelo
/// revendedor, mais o que entrou e saiu das competências seguintes. Entra o
/// recebido das vendas e renovações e o aporte de sócio; sai a taxa da Avalia,
/// a comissão repassada, a despesa, a retirada e o pedido de placas pago. A
/// tarifa do Pagar.me não passa pelo razão e fica de fora. O que veio antes do
/// saldo inicial continua no razão e no DRE dos meses dele, fora do caixa.
/// </summary>
public sealed class Caixa(IBancoDeDados banco, Fechamentos fechamentos, Auditoria.Auditoria auditoria, TimeProvider relogio)
{
    private static readonly TipoDeLancamento[] Entradas = [TipoDeLancamento.Receita, TipoDeLancamento.AporteDeSocio];

    private static readonly TipoDeLancamento[] Saidas =
        [TipoDeLancamento.TaxaAvalia, TipoDeLancamento.ComissaoPaga, TipoDeLancamento.Despesa, TipoDeLancamento.RetiradaDeSocio];

    public async Task<CaixaDto> Ler(CancellationToken ct)
    {
        if (await SaldoInicial(ct) is not { } inicial)
        {
            return new CaixaDto(null, 0, 0, 0, 0);
        }

        var tipos = Entradas.Concat(Saidas).ToArray();
        // O mês é texto de tamanho fixo (2026-11): a ordem do texto é a do tempo, e o corte é feito aqui, como no Dre.
        var linhas = (await banco.Lancamentos.AsNoTracking().Where(l => tipos.Contains(l.Tipo))
                .Select(l => new { l.Tipo, l.ValorCents, l.Competencia }).ToListAsync(ct))
            .Where(l => string.CompareOrdinal(l.Competencia, inicial.Competencia) >= 0).ToList();
        var desde = Lancamento.InicioDe(inicial.Competencia);
        var placas = await banco.Pedidos.AsNoTracking()
            .Where(p => p.PagoEm >= desde && p.Situacao != SituacaoDoPedido.Cancelado).SumAsync(p => p.ValorCents, ct);

        var entradas = linhas.Where(l => Entradas.Contains(l.Tipo)).Sum(l => l.ValorCents);
        var saidas = -linhas.Where(l => Saidas.Contains(l.Tipo)).Sum(l => l.ValorCents) + placas;
        return new CaixaDto(inicial.Competencia, inicial.ValorCents, entradas, saidas, inicial.ValorCents + entradas - saidas);
    }

    /// <summary>Um saldo inicial vale por vez: trocar estorna o anterior e lança o novo.</summary>
    public async Task<CaixaDto> DefinirSaldoInicial(NovoSaldoInicial pedido, CancellationToken ct)
    {
        var mes = Competencias.Conferir(pedido.Competencia);
        if (string.CompareOrdinal(mes, Competencias.Somar(fechamentos.MesCorrente(), 1)) > 0)
        {
            throw new RegraDeNegocioException("O caixa começa no mês corrente, no seguinte ou num mês que já passou.");
        }

        if (pedido.ValorCents is < 0 or > 100_000_000_00)
        {
            throw new RegraDeNegocioException("Informe o saldo inicial em reais, sem valor negativo.");
        }

        await fechamentos.ExigirAberto(mes, ct);
        var agora = relogio.GetUtcNow();
        var anterior = await SaldoInicial(ct);
        if (anterior is not null && anterior.Competencia == mes && anterior.ValorCents == pedido.ValorCents)
        {
            return await Ler(ct);
        }

        if (anterior is not null)
        {
            banco.Lancamentos.Add(anterior.Estorno("saldo inicial trocado", agora, await fechamentos.DestinoDaCorrecao(anterior.Competencia, ct)));
        }

        banco.Lancamentos.Add(new Lancamento(TipoDeLancamento.SaldoInicialDoCaixa, Guid.CreateVersion7(), pedido.ValorCents, "Saldo inicial do caixa",
            Lancamento.InicioDe(mes), competencia: mes));
        auditoria.Registrar(AcaoAuditada.SaldoInicialDoCaixaDefinido, "Saldo inicial do caixa", mes, anterior is null ? null : Resumo(anterior.Competencia, anterior.ValorCents),
            Resumo(mes, pedido.ValorCents));
        await banco.SaveChangesAsync(ct);
        return await Ler(ct);
    }

    public async Task ExcluirSaldoInicial(CancellationToken ct)
    {
        var anterior = await SaldoInicial(ct) ?? throw new RegraDeNegocioException("O caixa não tem saldo inicial.");
        banco.Lancamentos.Add(anterior.Estorno("saldo inicial excluído", relogio.GetUtcNow(), await fechamentos.DestinoDaCorrecao(anterior.Competencia, ct)));
        auditoria.Registrar(AcaoAuditada.SaldoInicialDoCaixaExcluido, "Saldo inicial do caixa", anterior.Competencia, Resumo(anterior.Competencia, anterior.ValorCents), null);
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>A linha de saldo inicial que nenhum estorno desfez.</summary>
    private Task<Lancamento?> SaldoInicial(CancellationToken ct) =>
        banco.Lancamentos.Where(l => l.Tipo == TipoDeLancamento.SaldoInicialDoCaixa && l.EstornaId == null && !banco.Lancamentos.Any(e => e.EstornaId == l.Id))
            .OrderByDescending(l => l.Id).FirstOrDefaultAsync(ct);

    private static string Resumo(string competencia, long valor) => $"R$ {valor / 100},{valor % 100:00} em {competencia}";
}
