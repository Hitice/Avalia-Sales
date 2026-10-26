using AvaliaSales.Application.Equipe;
using AvaliaSales.Domain.Acesso;
using AvaliaSales.Domain.Auditoria;
using AvaliaSales.Domain.Financeiro;
using AvaliaSales.Domain;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Financeiro;

/// <summary><paramref name="AguardandoConferenciaCents"/> - parte da comissão em aberto cuja venda foi recebida por fora e o dono ainda não conferiu: não entra em repasse.</summary>
public sealed record SaldoDeComissao(Guid VendedorId, string Vendedor, string? ChavePix, long DevidaCents, long RepassadaCents, long AbertaCents, DateTimeOffset? UltimoRepasse,
    long AguardandoConferenciaCents = 0);

public sealed record RepasseDto(Guid Id, Guid VendedorId, string Vendedor, long ValorCents, int Vendas, DateTimeOffset AteVendaEm, DateTimeOffset PagoEm, string? Comprovante,
    DateTimeOffset? DesfeitoEm = null, bool PodeDesfazer = false);

public sealed record NovoRepasse(Guid VendedorId, DateTimeOffset? Ate, string? Comprovante);

/// <summary>
/// O saldo de comissão de cada vendedor e o registro do repasse. O saldo sai
/// do razão: comissão devida das vendas pagas, menos estornos, menos o já
/// repassado. A comissão só existe quando o cliente paga.
/// </summary>
public sealed class Comissoes(IBancoDeDados banco, IUsuarioAtual usuario, IContas contas, RecebimentosPorFora porFora, Auditoria.Auditoria auditoria, Notificacoes.Notificacoes notificacoes, TimeProvider relogio)
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

        var aguardando = (await porFora.Pendencias(null, ct)).GroupBy(p => p.VendedorId)
            .ToDictionary(g => g.Key, g => g.Select(p => (Guid?)p.VendaId).ToList());
        var semConferir = await banco.Lancamentos.AsNoTracking()
            .Where(l => l.Tipo == TipoDeLancamento.ComissaoDevida && l.VendedorId != null && aguardando.Keys.Select(k => (Guid?)k).Contains(l.VendedorId))
            .Select(l => new { l.VendedorId, l.VendaId, l.ValorCents }).ToListAsync(ct);

        return vendedores.Select(v =>
        {
            var retida = aguardando.TryGetValue(v.Id, out var vendasRetidas)
                ? -semConferir.Where(l => l.VendedorId == v.Id && vendasRetidas.Contains(l.VendaId)).Sum(l => l.ValorCents)
                : 0;
            var devida = -linhas.Where(l => l.VendedorId == v.Id && l.Tipo == TipoDeLancamento.ComissaoDevida).Sum(l => l.Total);
            var repassada = -linhas.Where(l => l.VendedorId == v.Id && l.Tipo == TipoDeLancamento.ComissaoPaga).Sum(l => l.Total);
            return new SaldoDeComissao(v.Id, v.Nome, v.ChavePix, devida, repassada, devida - repassada, ultimos.TryGetValue(v.Id, out var u) ? u : null, retida);
        }).ToList();
    }

    /// <summary>Do mais novo ao mais antigo; a busca vale para o nome do vendedor e o comprovante. O vendedor vê só os dele.</summary>
    public async Task<Pagina<RepasseDto>> Repasses(string? busca, PedidoDePagina pedido, CancellationToken ct)
    {
        var consulta = banco.Repasses.AsNoTracking()
            .Where(r => usuario.Papel != Papel.Vendedor || r.VendedorId == usuario.UsuarioId);
        var termo = Paginacao.Termo(busca);
        if (termo.Length > 0)
        {
            consulta = consulta.Where(r => (r.Comprovante != null && Busca.Normalizar(r.Comprovante).Contains(Busca.Normalizar(termo)))
                || banco.Vendedores.Any(v => v.Id == r.VendedorId && Busca.Normalizar(v.Nome).Contains(Busca.Normalizar(termo))));
        }

        var pagina = await consulta.OrderByDescending(r => r.PagoEm).ThenByDescending(r => r.Id).PaginarAsync(pedido, ct);
        var vendedores = pagina.Itens.Select(r => r.VendedorId).Distinct().ToList();
        var nomes = await contas.Nomes(vendedores, ct);
        // Só o último repasse vivo de cada vendedor se desfaz (Desfazer), e a página pode não trazer o último dele.
        var ultimos = await banco.Repasses.AsNoTracking().Where(r => vendedores.Contains(r.VendedorId) && r.DesfeitoEm == null)
            .GroupBy(r => r.VendedorId).Select(g => new { g.Key, Ultimo = g.Max(r => r.PagoEm) }).ToDictionaryAsync(x => x.Key, x => x.Ultimo, ct);
        return pagina.Mapear(r => new RepasseDto(r.Id, r.VendedorId, nomes.GetValueOrDefault(r.VendedorId, ""), r.ValorCents, r.Vendas, r.AteVendaEm, r.PagoEm, r.Comprovante, r.DesfeitoEm,
            r.DesfeitoEm == null && ultimos.TryGetValue(r.VendedorId, out var ultimo) && r.PagoEm >= ultimo));
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

        // Venda recebida por fora que o dono não conferiu espera: a comissão dela fica fora deste repasse.
        var retidas = (await porFora.Pendencias(vendedor.Id, ct)).Select(p => (Guid?)p.VendaId).ToList();
        // O corte vale para a comissão, e o estorno dela vai junto mesmo depois do corte: a venda cancelada não é repassada.
        var originais = await banco.Lancamentos
            .Where(l => l.VendedorId == vendedor.Id && l.Tipo == TipoDeLancamento.ComissaoDevida && l.EstornaId == null && l.OcorridoEm <= ate && !retidas.Contains(l.VendaId))
            .ToListAsync(ct);
        var idsDasOriginais = originais.Select(l => (Guid?)l.Id).ToList();
        var estornos = await banco.Lancamentos
            .Where(l => l.VendedorId == vendedor.Id && l.Tipo == TipoDeLancamento.ComissaoDevida && idsDasOriginais.Contains(l.EstornaId))
            .ToListAsync(ct);
        var devidas = originais.Concat(estornos).ToList();
        var jaRepassado = -await banco.Lancamentos
            .Where(l => l.VendedorId == vendedor.Id && l.Tipo == TipoDeLancamento.ComissaoPaga)
            .SumAsync(l => l.ValorCents, ct);
        var valor = -devidas.Sum(l => l.ValorCents) - jaRepassado;
        var comissaoEmAberto = devidas.Where(d => d.VendaId != null).GroupBy(d => d.VendaId!.Value)
            .Where(g => g.Sum(d => d.ValorCents) < 0).Select(g => (Guid?)g.Key).ToList();
        // A venda cuja comissão o sistema anterior já pagou (linha paga com a venda) não entra na conta deste repasse.
        var vendas = await banco.Vendas
            .Where(v => v.VendedorId == vendedor.Id && v.RepasseId == null && comissaoEmAberto.Contains(v.Id)
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
        return new RepasseDto(repasse.Id, vendedor.Id, vendedor.Nome, valor, vendas.Count, ate, agora, repasse.Comprovante, null, true);
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
