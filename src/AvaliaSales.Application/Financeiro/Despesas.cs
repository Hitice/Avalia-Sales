using AvaliaSales.Domain;
using AvaliaSales.Domain.Financeiro;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Financeiro;

public sealed record DespesaDto(Guid Id, DateOnly Data, string Descricao, CategoriaDeDespesa Categoria, long ValorCents);

public sealed record NovaDespesa(DateOnly Data, string Descricao, CategoriaDeDespesa Categoria, long ValorCents);

/// <summary>
/// As despesas da revenda, com criar, editar e excluir. O registro muda; o
/// razão só recebe linha: editar estorna a linha em vigor e lança a nova, e
/// excluir só estorna. A linha nova tem origem própria (a versão), para o
/// razão aceitar mais de uma versão da mesma despesa.
/// </summary>
public sealed class Despesas(IBancoDeDados banco, Fechamentos fechamentos, TimeProvider relogio)
{
    /// <summary>As do mês, da data mais recente para trás; a busca vale para a descrição e a categoria.</summary>
    public async Task<Pagina<DespesaDto>> Listar(string? competencia, string? busca, PedidoDePagina pedido, CancellationToken ct)
    {
        var (de, ate) = Intervalo(string.IsNullOrWhiteSpace(competencia) ? fechamentos.MesCorrente() : Competencias.Conferir(competencia));
        var consulta = banco.Despesas.AsNoTracking().Where(d => d.Data >= de && d.Data < ate);
        var termo = Paginacao.Termo(busca);
        if (termo.Length > 0)
        {
            var categorias = Enum.GetValues<CategoriaDeDespesa>().Where(c => c.ToString().ToLowerInvariant().Contains(termo, StringComparison.Ordinal)).ToList();
            // Vira lower(...) LIKE no Postgres: a cultura do .NET não entra na consulta.
#pragma warning disable CA1304, CA1311, CA1862
            consulta = consulta.Where(d => d.Descricao.ToLower().Contains(termo) || categorias.Contains(d.Categoria));
#pragma warning restore CA1304, CA1311, CA1862
        }

        return await consulta.OrderByDescending(d => d.Data).ThenByDescending(d => d.CriadaEm).ThenByDescending(d => d.Id)
            .Select(d => new DespesaDto(d.Id, d.Data, d.Descricao, d.Categoria, d.ValorCents)).PaginarAsync(pedido, ct);
    }

    public async Task<DespesaDto> Criar(NovaDespesa pedido, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        var despesa = new Despesa(pedido.Data, pedido.Descricao, pedido.Categoria, pedido.ValorCents, Lancamento.DiaDe(agora), agora);
        await fechamentos.ExigirAberto(despesa.Competencia, ct);
        banco.Despesas.Add(despesa);
        Lancar(despesa, despesa.Competencia, agora);
        await banco.SaveChangesAsync(ct);
        return ParaDto(despesa);
    }

    /// <summary>Mudar a data para um mês fechado é recusado; corrigir despesa de mês fechado lança a correção no mês corrente.</summary>
    public async Task<DespesaDto> Alterar(Guid id, NovaDespesa pedido, CancellationToken ct)
    {
        var despesa = await Achar(id, ct);
        var antes = (despesa.Data, despesa.Descricao, despesa.Categoria, despesa.ValorCents);
        var agora = relogio.GetUtcNow();
        despesa.Alterar(pedido.Data, pedido.Descricao, pedido.Categoria, pedido.ValorCents, Lancamento.DiaDe(agora));
        if (antes == (despesa.Data, despesa.Descricao, despesa.Categoria, despesa.ValorCents))
        {
            return ParaDto(despesa);
        }

        if (Competencias.De(antes.Data) != despesa.Competencia)
        {
            await fechamentos.ExigirAberto(despesa.Competencia, ct);
        }

        await Estornar(despesa.LancamentoId, "despesa corrigida", agora, ct);
        Lancar(despesa, await fechamentos.DestinoDaCorrecao(despesa.Competencia, ct), agora);
        await banco.SaveChangesAsync(ct);
        return ParaDto(despesa);
    }

    public async Task Excluir(Guid id, CancellationToken ct)
    {
        var despesa = await Achar(id, ct);
        await Estornar(despesa.LancamentoId, "despesa excluída", relogio.GetUtcNow(), ct);
        banco.Despesas.Remove(despesa);
        await banco.SaveChangesAsync(ct);
    }

    private void Lancar(Despesa despesa, string competencia, DateTimeOffset agora)
    {
        var linha = new Lancamento(TipoDeLancamento.Despesa, Guid.CreateVersion7(), -despesa.ValorCents, $"{Rotulo(despesa.Categoria)} - {despesa.Descricao}", agora,
            competencia: competencia, categoria: despesa.Categoria.ToString());
        banco.Lancamentos.Add(linha);
        despesa.Lancada(linha.Id);
    }

    /// <summary>A linha em vigor sai no mês dela, se ainda aberto; de mês fechado, no corrente.</summary>
    private async Task Estornar(Guid lancamentoId, string motivo, DateTimeOffset agora, CancellationToken ct)
    {
        var linha = await banco.Lancamentos.SingleAsync(l => l.Id == lancamentoId, ct);
        banco.Lancamentos.Add(linha.Estorno(motivo, agora, await fechamentos.DestinoDaCorrecao(linha.Competencia, ct)));
    }

    private async Task<Despesa> Achar(Guid id, CancellationToken ct) =>
        await banco.Despesas.SingleOrDefaultAsync(d => d.Id == id, ct) ?? throw new RegraDeNegocioException("Despesa não encontrada.");

    internal static (DateOnly De, DateOnly Ate) Intervalo(string competencia)
    {
        var de = DateOnly.ParseExact(competencia + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        return (de, de.AddMonths(1));
    }

    public static string Rotulo(CategoriaDeDespesa categoria) => categoria switch
    {
        CategoriaDeDespesa.PlacasEMaterial => "Placas e material",
        CategoriaDeDespesa.Frete => "Frete",
        CategoriaDeDespesa.Marketing => "Marketing",
        CategoriaDeDespesa.SoftwareEFerramentas => "Software e ferramentas",
        CategoriaDeDespesa.Impostos => "Impostos",
        CategoriaDeDespesa.Taxas => "Taxas",
        _ => "Outros",
    };

    private static DespesaDto ParaDto(Despesa d) => new(d.Id, d.Data, d.Descricao, d.Categoria, d.ValorCents);
}
