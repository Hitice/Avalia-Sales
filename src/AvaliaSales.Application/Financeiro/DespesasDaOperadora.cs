using AvaliaSales.Domain;
using AvaliaSales.Domain.Financeiro;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Financeiro;

public sealed record DespesaDaOperadoraDto(Guid Id, DateOnly Data, string Descricao, CategoriaDeDespesaDaOperadora Categoria, long ValorCents);

public sealed record NovaDespesaDaOperadora(DateOnly Data, string Descricao, CategoriaDeDespesaDaOperadora Categoria, long ValorCents);

/// <summary>
/// As despesas da operadora, com criar, editar e excluir, como as da revenda. O registro muda; o
/// livro só recebe linha: editar estorna a linha em vigor e lança a nova, e excluir só estorna.
/// A linha nova tem origem própria (a versão), para o livro aceitar mais de uma versão da mesma despesa.
/// </summary>
public sealed class DespesasDaOperadora(IBancoDeDados banco, FechamentosDaOperadora fechamentos, TimeProvider relogio)
{
    /// <summary>As do mês, da data mais recente para trás; a busca vale para a descrição e a categoria.</summary>
    public async Task<Pagina<DespesaDaOperadoraDto>> Listar(string? competencia, string? busca, PedidoDePagina pedido, CancellationToken ct)
    {
        var (de, ate) = Despesas.Intervalo(string.IsNullOrWhiteSpace(competencia) ? fechamentos.MesCorrente() : Competencias.Conferir(competencia));
        var consulta = banco.DespesasDaOperadora.AsNoTracking().Where(d => d.Data >= de && d.Data < ate);
        var termo = Paginacao.Termo(busca);
        if (termo.Length > 0)
        {
            var categorias = Enum.GetValues<CategoriaDeDespesaDaOperadora>().Where(c => c.ToString().ToLowerInvariant().Contains(termo, StringComparison.Ordinal)).ToList();
            consulta = consulta.Where(d => Busca.Normalizar(d.Descricao).Contains(Busca.Normalizar(termo)) || categorias.Contains(d.Categoria));
        }

        return await consulta.OrderByDescending(d => d.Data).ThenByDescending(d => d.CriadaEm).ThenByDescending(d => d.Id)
            .Select(d => new DespesaDaOperadoraDto(d.Id, d.Data, d.Descricao, d.Categoria, d.ValorCents)).PaginarAsync(pedido, ct);
    }

    public async Task<DespesaDaOperadoraDto> Criar(NovaDespesaDaOperadora pedido, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        var despesa = new DespesaDaOperadora(pedido.Data, pedido.Descricao, pedido.Categoria, pedido.ValorCents, Lancamento.DiaDe(agora), agora);
        await fechamentos.ExigirAberto(despesa.Competencia, ct);
        banco.DespesasDaOperadora.Add(despesa);
        Lancar(despesa, despesa.Competencia, agora);
        await banco.SaveChangesAsync(ct);
        return ParaDto(despesa);
    }

    /// <summary>Mudar a data para um mês fechado é recusado; corrigir despesa de mês fechado lança a correção no mês corrente.</summary>
    public async Task<DespesaDaOperadoraDto> Alterar(Guid id, NovaDespesaDaOperadora pedido, CancellationToken ct)
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
        banco.DespesasDaOperadora.Remove(despesa);
        await banco.SaveChangesAsync(ct);
    }

    private void Lancar(DespesaDaOperadora despesa, string competencia, DateTimeOffset agora)
    {
        var linha = new LancamentoDaOperadora(TipoDeLancamentoDaOperadora.Despesa, Guid.CreateVersion7(), -despesa.ValorCents,
            $"{Rotulo(despesa.Categoria)} - {despesa.Descricao}", agora, competencia: competencia, categoria: despesa.Categoria.ToString());
        banco.LancamentosDaOperadora.Add(linha);
        despesa.Lancada(linha.Id);
    }

    /// <summary>A linha em vigor sai no mês dela, se ainda aberto; de mês fechado, no corrente.</summary>
    private async Task Estornar(Guid lancamentoId, string motivo, DateTimeOffset agora, CancellationToken ct)
    {
        var linha = await banco.LancamentosDaOperadora.SingleAsync(l => l.Id == lancamentoId, ct);
        banco.LancamentosDaOperadora.Add(linha.Estorno(motivo, agora, await fechamentos.DestinoDaCorrecao(linha.Competencia, ct)));
    }

    private async Task<DespesaDaOperadora> Achar(Guid id, CancellationToken ct) =>
        await banco.DespesasDaOperadora.SingleOrDefaultAsync(d => d.Id == id, ct) ?? throw new RegraDeNegocioException("Despesa não encontrada.");

    public static string Rotulo(CategoriaDeDespesaDaOperadora categoria) => categoria switch
    {
        CategoriaDeDespesaDaOperadora.GraficaEMaterial => "Gráfica e material",
        CategoriaDeDespesaDaOperadora.ExecucaoDeServico => "Execução de serviço",
        CategoriaDeDespesaDaOperadora.Frete => "Frete",
        CategoriaDeDespesaDaOperadora.SoftwareEFerramentas => "Software e ferramentas",
        CategoriaDeDespesaDaOperadora.Impostos => "Impostos",
        CategoriaDeDespesaDaOperadora.TarifaDoGateway => "Tarifa do gateway",
        _ => "Outros",
    };

    private static DespesaDaOperadoraDto ParaDto(DespesaDaOperadora d) => new(d.Id, d.Data, d.Descricao, d.Categoria, d.ValorCents);
}
