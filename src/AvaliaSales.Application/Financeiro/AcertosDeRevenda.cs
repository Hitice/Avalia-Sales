using AvaliaSales.Domain;
using AvaliaSales.Domain.Financeiro;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Financeiro;

/// <summary>Uma revenda com o que deve à Avalia por venda recebida por fora, o que já quitou e o que falta.</summary>
public sealed record SaldoAAcertarDto(Guid RevendedorId, string Revendedor, long DevidoCents, long AcertadoCents, long SaldoCents);

public sealed record AcertoDto(Guid Id, Guid RevendedorId, long ValorCents, DateOnly Data, string? Observacao, DateTimeOffset CriadoEm);

public sealed record NovoAcerto(Guid RevendedorId, long ValorCents, DateOnly Data, string? Observacao);

/// <summary>
/// O fechamento do "a acertar" (ADR 0025): a taxa e a parte da Avalia nos serviços que o split não repassou porque a venda
/// foi recebida por fora. O devido sai do livro da operadora (linhas marcadas por fora, já líquidas de estorno); a revenda
/// quita pagando à Avalia fora da plataforma, e a operadora registra aqui. Registrar não mexe no resultado: é caixa.
/// </summary>
public sealed class AcertosDeRevenda(IBancoDeDados banco, TimeProvider relogio, Revendedores.AcoesSensiveisDaOperadora sensiveis)
{
    /// <summary>As revendas com devido ou quitação, as de maior saldo primeiro; a busca vale para o nome. Com <paramref name="soComSaldo"/>, só as que ainda devem.</summary>
    public async Task<Pagina<SaldoAAcertarDto>> Saldos(string? busca, bool soComSaldo, PedidoDePagina pedido, CancellationToken ct)
    {
        var consulta = banco.Revendedores.AsNoTracking().Select(r => new
        {
            r.Id,
            r.Nome,
            Devido = banco.LancamentosDaOperadora.Where(l => l.RevendedorOrigemId == r.Id && l.PorFora).Sum(l => (long?)l.ValorCents) ?? 0,
            Acertado = banco.AcertosDeRevenda.Where(a => a.RevendedorId == r.Id).Sum(a => (long?)a.ValorCents) ?? 0,
        }).Where(x => x.Devido != 0 || x.Acertado != 0);
        var termo = Paginacao.Termo(busca);
        if (termo.Length > 0)
        {
            consulta = consulta.Where(x => Busca.Normalizar(x.Nome).Contains(Busca.Normalizar(termo)));
        }

        if (soComSaldo)
        {
            consulta = consulta.Where(x => x.Devido - x.Acertado > 0);
        }

        return (await consulta.OrderByDescending(x => x.Devido - x.Acertado).ThenBy(x => x.Nome).ThenBy(x => x.Id).PaginarAsync(pedido, ct))
            .Mapear(x => new SaldoAAcertarDto(x.Id, x.Nome, x.Devido, x.Acertado, x.Devido - x.Acertado));
    }

    public async Task<IReadOnlyList<AcertoDto>> DaRevenda(Guid revendedorId, CancellationToken ct) =>
        await banco.AcertosDeRevenda.AsNoTracking().Where(a => a.RevendedorId == revendedorId)
            .OrderByDescending(a => a.Data).ThenByDescending(a => a.CriadoEm).ThenByDescending(a => a.Id)
            .Select(a => new AcertoDto(a.Id, a.RevendedorId, a.ValorCents, a.Data, a.Observacao, a.CriadoEm)).ToListAsync(ct);

    /// <summary>Quitação não passa do que a revenda deve: o excedente seria dinheiro sem origem no livro.</summary>
    public async Task<AcertoDto> Registrar(NovoAcerto pedido, CancellationToken ct)
    {
        await sensiveis.Exigir("Registrar um acerto", ct);
        var revendedor = await banco.Revendedores.AsNoTracking().SingleOrDefaultAsync(r => r.Id == pedido.RevendedorId, ct)
            ?? throw new RegraDeNegocioException("Revendedor não encontrado.");
        var agora = relogio.GetUtcNow();
        var acerto = new AcertoDeRevenda(revendedor.Id, pedido.ValorCents, pedido.Data, pedido.Observacao, Lancamento.DiaDe(agora), agora);
        var devido = await banco.LancamentosDaOperadora.Where(l => l.RevendedorOrigemId == revendedor.Id && l.PorFora).SumAsync(l => (long?)l.ValorCents, ct) ?? 0;
        var acertado = await banco.AcertosDeRevenda.Where(a => a.RevendedorId == revendedor.Id).SumAsync(a => (long?)a.ValorCents, ct) ?? 0;
        if (acerto.ValorCents > devido - acertado)
        {
            throw new RegraDeNegocioException($"{revendedor.Nome} deve {Reais(devido - acertado)}. O acerto não pode passar disso.");
        }

        banco.AcertosDeRevenda.Add(acerto);
        await banco.SaveChangesAsync(ct);
        return new AcertoDto(acerto.Id, acerto.RevendedorId, acerto.ValorCents, acerto.Data, acerto.Observacao, acerto.CriadoEm);
    }

    /// <summary>Acerto lançado por engano: volta ao saldo devido.</summary>
    public async Task Excluir(Guid id, CancellationToken ct)
    {
        var acerto = await banco.AcertosDeRevenda.SingleOrDefaultAsync(a => a.Id == id, ct) ?? throw new RegraDeNegocioException("Acerto não encontrado.");
        banco.AcertosDeRevenda.Remove(acerto);
        await banco.SaveChangesAsync(ct);
    }

    private static string Reais(long centavos) => RecebimentosPorFora.Reais(Math.Max(0, centavos));
}
