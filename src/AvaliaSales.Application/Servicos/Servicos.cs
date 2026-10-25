using AvaliaSales.Domain;
using AvaliaSales.Domain.Servicos;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Servicos;

public sealed record OrdemDto(Guid Id, long Numero, string Revendedor, string Produto, string Cliente, SituacaoDaOrdem Situacao, string? Observacao,
    DateTimeOffset CriadaEm, DateTimeOffset? IniciadaEm, DateTimeOffset? EntregueEm, int Quantidade = 1);

public sealed record AnotarOrdem(string? Observacao);

/// <summary>
/// As ordens de serviço: a operadora executa e anda com elas; o revendedor
/// acompanha as dele. Cada passo avisa o dono no sino.
/// </summary>
public sealed class Servicos(IBancoDeDados banco, IRevendedorAtual atual, Notificacoes.Notificacoes notificacoes, TimeProvider relogio)
{
    /// <summary>
    /// A fila primeiro (a fazer e em andamento, da mais antiga); o encerrado depois, da mais recente. A busca vale
    /// para o produto, o cliente, o número e o nome do revendedor.
    /// </summary>
    public async Task<Pagina<OrdemDto>> Listar(string? busca, SituacaoDaOrdem? situacao, PedidoDePagina pedido, CancellationToken ct)
    {
        var consulta = banco.OrdensDeServico.AsNoTracking();
        if (situacao is { } desta)
        {
            consulta = consulta.Where(o => o.Situacao == desta);
        }

        var termo = Paginacao.Termo(busca);
        if (termo.Length > 0)
        {
            long? numero = long.TryParse(new string(termo.Where(char.IsAsciiDigit).ToArray()), out var lido) ? lido : null;
            // Vira lower(...) LIKE no Postgres: a cultura do .NET não entra na consulta.
#pragma warning disable CA1304, CA1311, CA1862
            consulta = consulta.Where(o => o.Produto.ToLower().Contains(termo) || o.Cliente.ToLower().Contains(termo)
                || (numero != null && o.Numero == numero)
                || banco.Revendedores.Any(r => r.Id == o.RevendedorId && r.Nome.ToLower().Contains(termo)));
#pragma warning restore CA1304, CA1311, CA1862
        }

        var pagina = await consulta
            .OrderBy(o => o.Situacao == SituacaoDaOrdem.AFazer || o.Situacao == SituacaoDaOrdem.EmAndamento ? 0 : 1)
            .ThenBy(o => o.Situacao == SituacaoDaOrdem.AFazer || o.Situacao == SituacaoDaOrdem.EmAndamento ? o.CriadaEm : DateTimeOffset.MaxValue)
            .ThenByDescending(o => o.EntregueEm ?? o.CriadaEm)
            .ThenBy(o => o.Id)
            .PaginarAsync(pedido, ct);
        var ids = pagina.Itens.Select(o => o.RevendedorId).Distinct().ToList();
        var nomes = await banco.Revendedores.AsNoTracking().Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Nome, ct);
        return pagina.Mapear(o => new OrdemDto(o.Id, o.Numero, nomes.GetValueOrDefault(o.RevendedorId, ""), o.Produto, o.Cliente, o.Situacao, o.Observacao,
            o.CriadaEm, o.IniciadaEm, o.EntregueEm, o.Quantidade));
    }

    public Task Iniciar(Guid id, CancellationToken ct) => Mudar(id, (o, agora) => o.Iniciar(agora), "Serviço em andamento", "A Avalia começou", ct);

    public Task Entregar(Guid id, CancellationToken ct) => Mudar(id, (o, agora) => o.Entregar(agora), "Serviço entregue", "A Avalia entregou", ct);

    public Task Cancelar(Guid id, CancellationToken ct) => Mudar(id, (o, _) => o.Cancelar(), "Serviço cancelado", "A Avalia cancelou", ct);

    /// <summary>Cancelada por estorno não volta: sem pagamento, o serviço não se executa.</summary>
    public async Task Reabrir(Guid id, CancellationToken ct)
    {
        var ordem = await banco.OrdensDeServico.SingleOrDefaultAsync(o => o.Id == id, ct) ?? throw new RegraDeNegocioException("Ordem não encontrada.");
        if (ordem.Situacao == SituacaoDaOrdem.Cancelada)
        {
            // No grupo, a cobrança é a da primeira venda; a venda cancelada sozinha num grupo pago também não volta.
            var venda = await banco.Vendas.Where(v => v.Id == ordem.VendaId).Select(v => new { CobradaPor = v.GrupoId ?? v.Id, v.Situacao }).SingleOrDefaultAsync(ct);
            var cobradaPor = venda?.CobradaPor ?? ordem.VendaId;
            if (venda?.Situacao == Domain.Vendas.SituacaoDaVenda.Cancelada || !await banco.Cobrancas.AnyAsync(c => c.Origem == Domain.Pagamentos.OrigemDaCobranca.Venda
                    && c.OrigemId == cobradaPor && c.Situacao == Domain.Pagamentos.SituacaoDaCobranca.Paga, ct))
            {
                throw new RegraDeNegocioException("A venda deste serviço não está paga.");
            }
        }

        await Mudar(id, (o, _) => o.Reabrir(), "Serviço reaberto", "A Avalia reabriu", ct);
    }

    public async Task Anotar(Guid id, string? observacao, CancellationToken ct)
    {
        (await Achar(id, ct)).Anotar(observacao);
        await banco.SaveChangesAsync(ct);
    }

    private async Task Mudar(Guid id, Action<OrdemDeServico, DateTimeOffset> mudar, string titulo, string acao, CancellationToken ct)
    {
        if (!atual.Operadora)
        {
            throw new RegraDeNegocioException("Só a Avalia anda com a ordem de serviço.");
        }

        var ordem = await Achar(id, ct);
        mudar(ordem, relogio.GetUtcNow());
        notificacoes.ParaDono(ordem.RevendedorId, titulo, $"{acao} {ordem.Produto} de {ordem.Cliente} (ordem {ordem.Numero}).", "/links?aba=status");
        banco.AvisarAoVivo("servico", ordem.RevendedorId, operadora: true);
        await banco.SaveChangesAsync(ct);
    }

    private async Task<OrdemDeServico> Achar(Guid id, CancellationToken ct) =>
        await banco.OrdensDeServico.SingleOrDefaultAsync(o => o.Id == id, ct) ?? throw new RegraDeNegocioException("Ordem de serviço não encontrada.");
}
