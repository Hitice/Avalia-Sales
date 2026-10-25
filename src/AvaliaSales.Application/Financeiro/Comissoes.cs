using AvaliaSales.Application.Equipe;
using AvaliaSales.Domain.Acesso;
using AvaliaSales.Domain.Auditoria;
using AvaliaSales.Domain.Financeiro;
using AvaliaSales.Domain;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Financeiro;

public sealed record SaldoDeComissao(Guid VendedorId, string Vendedor, string? ChavePix, long DevidaCents, long RepassadaCents, long AbertaCents, DateTimeOffset? UltimoRepasse);

public sealed record RepasseDto(Guid Id, Guid VendedorId, string Vendedor, long ValorCents, int Vendas, DateTimeOffset AteVendaEm, DateTimeOffset PagoEm, string? Comprovante,
    DateTimeOffset? DesfeitoEm = null);

public sealed record NovoRepasse(Guid VendedorId, DateTimeOffset? Ate, string? Comprovante);

/// <summary>
/// O saldo de comissão de cada vendedor e o registro do repasse. O saldo sai
/// do razão: comissão devida das vendas pagas, menos estornos, menos o já
/// repassado. A comissão só existe quando o cliente paga.
/// </summary>
public sealed class Comissoes(IBancoDeDados banco, IUsuarioAtual usuario, IContas contas, Auditoria.Auditoria auditoria, Notificacoes.Notificacoes notificacoes, TimeProvider relogio)
{
    public async Task<IReadOnlyList<SaldoDeComissao>> Saldos(CancellationToken ct)
    {
        var vendedores = await banco.Vendedores.AsNoTracking()
            .Where(v => usuario.Papel != Papel.Vendedor || v.Id == usuario.UsuarioId)
            .OrderBy(v => v.Nome).ToListAsync(ct);
        var ids = vendedores.Select(v => (Guid?)v.Id).ToList();
        var linhas = await banco.Lancamentos.AsNoTracking()
            .Where(l => ids.Contains(l.VendedorId) && (l.Tipo == TipoDeLancamento.ComissaoDevida || l.Tipo == TipoDeLancamento.ComissaoPaga))
            .GroupBy(l => new { l.VendedorId, l.Tipo })
            .Select(g => new { g.Key.VendedorId, g.Key.Tipo, Total = g.Sum(l => l.ValorCents) })
            .ToListAsync(ct);
        var ultimos = await banco.Repasses.AsNoTracking().GroupBy(r => r.VendedorId)
            .Select(g => new { g.Key, Ultimo = g.Max(r => r.PagoEm) }).ToDictionaryAsync(x => x.Key, x => x.Ultimo, ct);

        return vendedores.Select(v =>
        {
            var devida = -linhas.Where(l => l.VendedorId == v.Id && l.Tipo == TipoDeLancamento.ComissaoDevida).Sum(l => l.Total);
            var repassada = -linhas.Where(l => l.VendedorId == v.Id && l.Tipo == TipoDeLancamento.ComissaoPaga).Sum(l => l.Total);
            return new SaldoDeComissao(v.Id, v.Nome, v.ChavePix, devida, repassada, devida - repassada, ultimos.TryGetValue(v.Id, out var u) ? u : null);
        }).ToList();
    }

    public async Task<IReadOnlyList<RepasseDto>> Repasses(CancellationToken ct)
    {
        var repasses = await banco.Repasses.AsNoTracking()
            .Where(r => usuario.Papel != Papel.Vendedor || r.VendedorId == usuario.UsuarioId)
            .OrderByDescending(r => r.PagoEm).Take(200).ToListAsync(ct);
        var nomes = await contas.Nomes(repasses.Select(r => r.VendedorId), ct);
        return repasses.Select(r => new RepasseDto(r.Id, r.VendedorId, nomes.GetValueOrDefault(r.VendedorId, ""), r.ValorCents, r.Vendas, r.AteVendaEm, r.PagoEm, r.Comprovante, r.DesfeitoEm)).ToList();
    }

    /// <summary>
    /// Registra o Pix feito ao vendedor: paga a comissão devida até a data de
    /// corte, menos o que já foi repassado. Tudo ou nada, com o vendedor
    /// travado: dois cliques no dia do pagamento não pagam a mesma venda duas vezes.
    /// </summary>
    public async Task<RepasseDto> Repassar(NovoRepasse pedido, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        var ate = pedido.Ate is { } corte && corte < agora ? corte : agora;
        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        await banco.TravarVendedor(pedido.VendedorId, ct);
        var vendedor = await banco.Vendedores.AsNoTracking().SingleOrDefaultAsync(v => v.Id == pedido.VendedorId, ct)
            ?? throw new RegraDeNegocioException("Vendedor não encontrado.");

        var devidas = await banco.Lancamentos
            .Where(l => l.VendedorId == vendedor.Id && l.Tipo == TipoDeLancamento.ComissaoDevida && l.OcorridoEm <= ate)
            .ToListAsync(ct);
        var jaRepassado = -await banco.Lancamentos
            .Where(l => l.VendedorId == vendedor.Id && l.Tipo == TipoDeLancamento.ComissaoPaga)
            .SumAsync(l => l.ValorCents, ct);
        var valor = -devidas.Sum(l => l.ValorCents) - jaRepassado;
        // A venda cuja comissão o sistema anterior já pagou (linha paga com a venda) não entra na conta deste repasse.
        var vendas = await banco.Vendas
            .Where(v => v.VendedorId == vendedor.Id && v.RepasseId == null && devidas.Select(d => d.VendaId).Contains(v.Id)
                && !banco.Lancamentos.Any(l => l.VendaId == v.Id && l.Tipo == TipoDeLancamento.ComissaoPaga))
            .ToListAsync(ct);

        var repasse = new Repasse(vendedor.Id, valor, vendas.Count, ate, agora, pedido.Comprovante);
        foreach (var venda in vendas)
        {
            venda.MarcarRepassada(repasse.Id);
        }

        banco.Repasses.Add(repasse);
        auditoria.Registrar(AcaoAuditada.RepasseRegistrado, $"Vendedor {vendedor.Nome}", vendedor.Id.ToString(), null,
            $"{valor} centavos, {vendas.Count} vendas, comprovante {pedido.Comprovante ?? "-"}");
        banco.AvisarAoVivo("comissao", vendedor.RevendedorId);
        banco.Lancamentos.Add(new Lancamento(TipoDeLancamento.ComissaoPaga, repasse.Id, -valor, $"Comissão repassada a {vendedor.Nome}", agora, vendedorId: vendedor.Id));
        notificacoes.ParaVendedor(vendedor.RevendedorId, vendedor.Id, "Comissão paga",
            $"A revenda repassou R$ {valor / 100},{valor % 100:00} de {vendas.Count} {(vendas.Count == 1 ? "venda" : "vendas")}.", "/vendedor/inicio");
        await banco.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
        return new RepasseDto(repasse.Id, vendedor.Id, vendedor.Nome, valor, vendas.Count, ate, agora, repasse.Comprovante);
    }

    /// <summary>
    /// Desfaz o último repasse do vendedor, registrado por engano: a comissão
    /// paga é estornada no razão e as vendas voltam ao próximo repasse. Só o
    /// último, porque o seguinte foi calculado contando com ele.
    /// </summary>
    public async Task Desfazer(Guid repasseId, CancellationToken ct)
    {
        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        var repasse = await banco.Repasses.SingleOrDefaultAsync(r => r.Id == repasseId, ct) ?? throw new RegraDeNegocioException("Repasse não encontrado.");
        await banco.TravarVendedor(repasse.VendedorId, ct);
        if (await banco.Repasses.AnyAsync(r => r.VendedorId == repasse.VendedorId && r.DesfeitoEm == null && r.PagoEm > repasse.PagoEm, ct))
        {
            throw new RegraDeNegocioException("Só o último repasse do vendedor se desfaz.");
        }

        var agora = relogio.GetUtcNow();
        repasse.Desfazer(agora);
        foreach (var linha in await banco.Lancamentos.Where(l => l.OrigemId == repasse.Id && l.Tipo == TipoDeLancamento.ComissaoPaga && l.EstornaId == null).ToListAsync(ct))
        {
            banco.Lancamentos.Add(linha.Estorno("repasse desfeito", agora));
        }

        foreach (var venda in await banco.Vendas.Where(v => v.RepasseId == repasse.Id).ToListAsync(ct))
        {
            venda.DesmarcarRepasse(repasse.Id);
        }

        auditoria.Registrar(AcaoAuditada.RepasseDesfeito, "Repasse", repasse.Id.ToString(), $"{repasse.ValorCents} centavos", null);
        banco.AvisarAoVivo("comissao", repasse.RevendedorId);
        notificacoes.ParaVendedor(repasse.RevendedorId, repasse.VendedorId, "Repasse desfeito",
            $"O repasse de R$ {repasse.ValorCents / 100},{repasse.ValorCents % 100:00} foi desfeito pela revenda.", "/vendedor/inicio");
        await banco.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
    }
}
