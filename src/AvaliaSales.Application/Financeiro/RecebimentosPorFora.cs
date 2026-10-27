using System.Globalization;
using System.Text.Json;
using AvaliaSales.Application.Equipe;
using AvaliaSales.Domain;
using AvaliaSales.Domain.Acesso;
using AvaliaSales.Domain.Auditoria;
using AvaliaSales.Domain.Financeiro;
using AvaliaSales.Domain.Pagamentos;
using AvaliaSales.Domain.Vendas;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Financeiro;

/// <summary>O que o vendedor pode registrar como recebido por fora (dinheiro, outro Pix): por dia, em valor e em quantidade, e por venda.</summary>
public sealed record LimitesDePorFora(long DiaCents, int DiaQuantidade, long VendaCents);

/// <summary>Um recebimento por fora que o dono ainda não conferiu; a comissão dele não entra em repasse até a conferência.</summary>
public sealed record PendenteDeConferencia(Guid CobrancaId, Guid VendaId, Guid VendedorId, string Vendedor, string Cliente, MeioDePagamento Meio,
    long ValorCents, long ComissaoCents, DateTimeOffset RecebidoEm);

/// <summary><paramref name="CobrancaIds"/> - os recebimentos conferidos; nulo ou vazio confere todos os pendentes.</summary>
public sealed record ConferirRecebimentos(IReadOnlyList<Guid>? CobrancaIds);

public sealed record ResultadoDaConferencia(int Conferidos);

/// <summary>
/// O recebimento por fora tem rastro, limite e conferência. O vendedor registra
/// no balcão; o dono é avisado na hora, o servidor recusa acima do limite do
/// dia e da venda, e a comissão só entra em repasse depois que o dono confere.
/// Limite e conferência ficam na auditoria, que só recebe inserção: o último
/// limite do vendedor vale, e a conferência é o registro da cobrança.
/// </summary>
public sealed class RecebimentosPorFora(IBancoDeDados banco, IUsuarioAtual usuario, IRevendedorAtual atual, IContas contas, Auditoria.Auditoria auditoria,
    Notificacoes.Notificacoes notificacoes, TimeProvider relogio)
{
    /// <summary>Generosos de propósito: o balcão não para por causa do limite, e o dono aperta se quiser.</summary>
    public static readonly LimitesDePorFora Padrao = new(DiaCents: 1_000_000, DiaQuantidade: 30, VendaCents: 500_000);

    private const long LimiteMaximoCents = 10_000_000_000;
    private const int QuantidadeMaxima = 10_000;
    private static readonly TimeZoneInfo Brasilia = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<LimitesDePorFora> Limites(Guid vendedorId, CancellationToken ct) =>
        (await LimitesDe([vendedorId], ct)).GetValueOrDefault(vendedorId, Padrao);

    public async Task<IReadOnlyDictionary<Guid, LimitesDePorFora>> LimitesDe(IEnumerable<Guid> vendedores, CancellationToken ct)
    {
        var referencias = vendedores.Select(v => v.ToString()).Distinct().ToList();
        var registros = await banco.Auditoria.AsNoTracking()
            .Where(a => a.Acao == AcaoAuditada.LimiteDePorForaDefinido && referencias.Contains(a.Referencia))
            .OrderBy(a => a.OcorridoEm).Select(a => new { a.Referencia, a.Depois }).ToListAsync(ct);
        var limites = new Dictionary<Guid, LimitesDePorFora>();
        foreach (var r in registros)
        {
            if (Guid.TryParse(r.Referencia, out var id) && r.Depois is not null && JsonSerializer.Deserialize<LimitesDePorFora>(r.Depois, Json) is { } lido)
            {
                limites[id] = lido;
            }
        }

        return limites;
    }

    public async Task DefinirLimites(Guid vendedorId, LimitesDePorFora novos, CancellationToken ct)
    {
        if (novos.DiaCents is < 0 or > LimiteMaximoCents || novos.VendaCents is < 0 or > LimiteMaximoCents || novos.DiaQuantidade is < 0 or > QuantidadeMaxima)
        {
            throw new RegraDeNegocioException("Limites de zero a R$ 100.000.000,00 e até 10.000 por dia.");
        }

        var vendedor = await banco.Vendedores.AsNoTracking().SingleOrDefaultAsync(v => v.Id == vendedorId, ct)
            ?? throw new RegraDeNegocioException("Vendedor não encontrado.");
        var antes = await Limites(vendedorId, ct);
        if (antes == novos)
        {
            return;
        }

        auditoria.Registrar(AcaoAuditada.LimiteDePorForaDefinido, $"Vendedor {vendedor.Nome}", vendedorId.ToString(),
            JsonSerializer.Serialize(antes, Json), JsonSerializer.Serialize(novos, Json));
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Recusa o recebimento que passa do limite do dia (valor ou quantidade) ou do teto da venda.
    /// Vale para o vendedor; o dono não tem limite. Quem chama já travou o vendedor, para dois
    /// cliques seguidos não passarem juntos.
    /// </summary>
    public async Task ExigirDentroDoLimite(Guid vendedorId, long valorCents, CancellationToken ct)
    {
        if (usuario.Papel != Papel.Vendedor)
        {
            return;
        }

        var limites = await Limites(vendedorId, ct);
        if (valorCents > limites.VendaCents)
        {
            throw new RegraDeNegocioException($"Pagamento por fora de até {Reais(limites.VendaCents)} por venda. Chame o revendedor.");
        }

        var agora = relogio.GetUtcNow();
        var local = TimeZoneInfo.ConvertTime(agora, Brasilia);
        var inicio = new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, local.Offset).ToUniversalTime();
        var hoje = await DoDia(vendedorId, inicio, ct);
        if (hoje.Quantidade + 1 > limites.DiaQuantidade)
        {
            throw new RegraDeNegocioException($"Limite de {limites.DiaQuantidade} pagamentos por fora no dia atingido. Chame o revendedor.");
        }

        if (hoje.ValorCents + valorCents > limites.DiaCents)
        {
            throw new RegraDeNegocioException($"Limite de {Reais(limites.DiaCents)} por fora no dia atingido ({Reais(hoje.ValorCents)} já registrados). Chame o revendedor.");
        }
    }

    /// <summary>As vendas do vendedor recebidas por fora hoje e que seguem de pé: o desfeito libera o limite.</summary>
    private async Task<(int Quantidade, long ValorCents)> DoDia(Guid vendedorId, DateTimeOffset inicio, CancellationToken ct)
    {
        var porFora = banco.Cobrancas.AsNoTracking()
            .Where(c => c.Situacao == SituacaoDaCobranca.Paga && c.PagaEm >= inicio && (c.Meio == MeioDePagamento.Dinheiro || c.Meio == MeioDePagamento.OutroPix));
        var deVenda = from c in porFora.Where(c => c.Origem == OrigemDaCobranca.Venda)
                      where banco.Vendas.Any(v => (v.GrupoId ?? v.Id) == c.OrigemId && v.VendedorId == vendedorId)
                      select c.ValorCents;
        // A renovação é só do revendedor e não gasta a cota do vendedor.
        var valores = await deVenda.ToListAsync(ct);
        return (valores.Count, valores.Sum());
    }

    /// <summary>
    /// Depois de registrar: o dono é avisado na hora, com o atalho para a tela de desfazer. O que o
    /// próprio dono registra já nasce conferido; o do vendedor espera a conferência.
    /// </summary>
    public async Task Registrado(Cobranca cobranca, Guid vendedorId, string texto, string link, Guid vendaId, CancellationToken ct)
    {
        if (usuario.Papel != Papel.Vendedor)
        {
            auditoria.Registrar(AcaoAuditada.RecebimentoConferido, "Recebimento por fora", cobranca.Id.ToString(), null, "registrado pelo revendedor");
            return;
        }

        var vendedor = (await contas.Nomes([vendedorId], ct)).GetValueOrDefault(vendedorId, "Um vendedor");
        var meio = cobranca.Meio == MeioDePagamento.Dinheiro ? "em dinheiro" : "por outro Pix";
        notificacoes.ParaDono(cobranca.RevendedorId, "Recebido por fora", $"{vendedor} registrou {Reais(cobranca.ValorCents)} recebido {meio} {texto}.", link, vendaId);
    }

    /// <summary>Os recebimentos por fora de venda ainda não conferidos, do mais antigo ao mais novo. O vendedor só vê os dele.</summary>
    public async Task<IReadOnlyList<PendenteDeConferencia>> Pendentes(CancellationToken ct)
    {
        var linhas = await Pendencias(null, ct);
        var comissoes = await ComissaoDasVendas(linhas.Select(l => l.VendaId), ct);
        return linhas.GroupBy(l => l.CobrancaId).Select(g =>
        {
            var primeira = g.First();
            return new PendenteDeConferencia(g.Key, primeira.VendaId, primeira.VendedorId, primeira.Vendedor, primeira.Cliente, primeira.Meio,
                primeira.ValorDaCobrancaCents, g.Sum(l => comissoes.GetValueOrDefault(l.VendaId)), primeira.RecebidoEm);
        }).OrderBy(p => p.RecebidoEm).ToList();
    }

    /// <summary>O dono viu: um registro por recebimento. Sem lista, confere todos os pendentes.</summary>
    public async Task<ResultadoDaConferencia> Conferir(ConferirRecebimentos pedido, CancellationToken ct)
    {
        var escolhidos = pedido.CobrancaIds is { Count: > 0 } ids ? ids.ToHashSet() : null;
        var pendentes = (await Pendentes(ct)).Where(p => escolhidos is null || escolhidos.Contains(p.CobrancaId)).ToList();
        foreach (var p in pendentes)
        {
            auditoria.Registrar(AcaoAuditada.RecebimentoConferido, $"Recebimento por fora de {p.Cliente}", p.CobrancaId.ToString(), null,
                $"{Reais(p.ValorCents)} {(p.Meio == MeioDePagamento.Dinheiro ? "em dinheiro" : "por outro Pix")}, vendedor {p.Vendedor}");
        }

        if (pendentes.Count > 0)
        {
            banco.AvisarAoVivo("comissao", atual.RevendedorId);
            await banco.SaveChangesAsync(ct);
        }

        return new ResultadoDaConferencia(pendentes.Count);
    }

    /// <summary>As vendas do vendedor, ou de todos, cujo recebimento por fora o dono não conferiu e cuja comissão ainda não foi repassada.</summary>
    public async Task<IReadOnlyList<Pendencia>> Pendencias(Guid? vendedorId, CancellationToken ct)
    {
        var so = usuario.Papel == Papel.Vendedor ? usuario.UsuarioId : vendedorId;
        var consulta = banco.Vendas.AsNoTracking().Where(v => v.RepasseId == null && v.Situacao == SituacaoDaVenda.Registrada && !v.Cortesia);
        if (so is { } apenas)
        {
            consulta = consulta.Where(v => v.VendedorId == apenas);
        }

        var linhas = await (
            from v in consulta
            join vd in banco.Vendedores on v.VendedorId equals vd.Id
            join cl in banco.Clientes on v.ClienteId equals cl.Id
            from c in banco.Cobrancas.Where(x => x.Origem == OrigemDaCobranca.Venda && x.OrigemId == (v.GrupoId ?? v.Id) && x.Situacao == SituacaoDaCobranca.Paga
                && (x.Meio == MeioDePagamento.Dinheiro || x.Meio == MeioDePagamento.OutroPix))
            select new { VendaId = v.Id, v.VendedorId, Vendedor = vd.Nome, Cliente = cl.Nome, CobrancaId = c.Id, c.Meio, c.ValorCents, RecebidoEm = c.PagaEm })
            .ToListAsync(ct);
        if (linhas.Count == 0)
        {
            return [];
        }

        var referencias = linhas.Select(l => l.CobrancaId.ToString()).Distinct().ToList();
        var conferidas = (await banco.Auditoria.AsNoTracking().Where(a => a.Acao == AcaoAuditada.RecebimentoConferido && referencias.Contains(a.Referencia))
            .Select(a => a.Referencia).ToListAsync(ct)).ToHashSet();
        return linhas.Where(l => !conferidas.Contains(l.CobrancaId.ToString()))
            .Select(l => new Pendencia(l.VendaId, l.VendedorId, l.Vendedor, l.Cliente, l.CobrancaId, l.Meio, l.ValorCents, l.RecebidoEm ?? DateTimeOffset.MinValue)).ToList();
    }

    /// <summary>A comissão devida (razão, com os estornos) das vendas, por venda.</summary>
    public async Task<IReadOnlyDictionary<Guid, long>> ComissaoDasVendas(IEnumerable<Guid> vendas, CancellationToken ct)
    {
        var ids = vendas.Select(v => (Guid?)v).ToList();
        var linhas = await banco.Lancamentos.AsNoTracking().Where(l => l.Tipo == TipoDeLancamento.ComissaoDevida && ids.Contains(l.VendaId))
            .GroupBy(l => l.VendaId).Select(g => new { VendaId = g.Key, Total = g.Sum(l => l.ValorCents) }).ToListAsync(ct);
        return linhas.Where(l => l.VendaId is not null).ToDictionary(l => l.VendaId!.Value, l => -l.Total);
    }

    public static string Reais(long centavos) => string.Create(CultureInfo.GetCultureInfo("pt-BR"), $"R$ {centavos / 100:N0},{centavos % 100:00}");

    /// <summary>Uma venda de pé cujo recebimento por fora espera o dono.</summary>
    public sealed record Pendencia(Guid VendaId, Guid VendedorId, string Vendedor, string Cliente, Guid CobrancaId, MeioDePagamento Meio, long ValorDaCobrancaCents,
        DateTimeOffset RecebidoEm);
}
