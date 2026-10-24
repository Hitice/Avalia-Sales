using AvaliaSales.Domain;
using AvaliaSales.Domain.Auditoria;
using AvaliaSales.Domain.Catalogo;
using AvaliaSales.Domain.Placas;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Catalogo;

/// <summary>
/// Um produto como a revenda vê: o do catálogo da Avalia com o preço dela.
/// <paramref name="Id"/> é o do produto da Avalia. O produto antigo, de antes
/// do catálogo, não aparece: só as vendas feitas com ele continuam apontando para ele.
/// </summary>
public sealed record ItemDoCatalogo(Guid Id, string Chave, string Nome, string Descricao, TipoDeProduto Tipo, SituacaoDoProduto Situacao,
    string? FotoUrl, long PrecoSugeridoCents, long PrecoCents, int? ComissaoBps, long PisoCents, bool Ativo, int QuantidadeMinima = 1);

public sealed record AjustarProduto(long PrecoCents, int? ComissaoBps);

/// <summary>
/// O catálogo da Avalia visto pela revenda. O revendedor não cria produto:
/// define o preço e a comissão dele, nunca abaixo do custo. A equipe lê para
/// vender. O filtro por revendedor fica na infraestrutura.
/// </summary>
public sealed class Catalogo(IBancoDeDados banco, IUsuarioAtual usuario, Auditoria.Auditoria auditoria)
{
    public async Task<IReadOnlyList<ItemDoCatalogo>> Listar(CancellationToken ct)
    {
        var daAvalia = await banco.ProdutosDaPlataforma.AsNoTracking()
            .Where(p => p.Situacao != SituacaoDoProduto.ForaDeLinha)
            .OrderBy(p => p.Ordem).ThenBy(p => p.Nome)
            .ToListAsync(ct);
        var daRevenda = await banco.Produtos.AsNoTracking().ToListAsync(ct);
        // O custo é do dono: o vendedor vê o preço de venda, não o que a revenda pagou.
        var dono = usuario.Papel == Domain.Acesso.Papel.Dono;
        var pisoDaPlaca = dono ? await PisoDaPlaca(ct) : 0;

        return daAvalia.Select(p =>
        {
            var meu = daRevenda.FirstOrDefault(r => r.ProdutoDaPlataformaId == p.Id);
            return new ItemDoCatalogo(p.Id, p.Chave, p.Nome, p.Descricao, p.Tipo, p.Situacao, FotoUrl(p), p.PrecoSugeridoCents,
                meu?.PrecoCents ?? p.PrecoSugeridoCents, meu?.ComissaoBps, !dono ? 0 : Piso(p, pisoDaPlaca), meu?.Ativo ?? true, p.QuantidadeMinima);
        }).ToList();
    }

    public async Task<ItemDoCatalogo> Ajustar(Guid id, AjustarProduto comando, CancellationToken ct)
    {
        var (produto, doCatalogo) = await Garantir(id, ct);
        var piso = doCatalogo is { UsaPlaca: false } ? Piso(doCatalogo, 0) : doCatalogo is null ? await PisoDaPlaca(ct) : Piso(doCatalogo, await PisoDaPlaca(ct));
        var antes = Resumo(produto);
        produto.AjustarPreco(doCatalogo?.Nome ?? produto.Nome, comando.PrecoCents, comando.ComissaoBps, piso);
        if (Resumo(produto) != antes)
        {
            auditoria.Registrar(AcaoAuditada.ProdutoAlterado, $"Produto {produto.Nome}", produto.Id.ToString(), antes, Resumo(produto));
        }

        await banco.SaveChangesAsync(ct);
        return (await Listar(ct)).Single(i => i.Id == id);
    }

    public async Task DefinirAtivo(Guid id, bool ativo, CancellationToken ct)
    {
        (await Garantir(id, ct)).Produto.DefinirAtivo(ativo);
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>O menor preço da revenda: a placa que sai do estoque e a parte da Avalia no serviço.</summary>
    private static long Piso(ProdutoDaPlataforma p, long pisoDaPlaca) => Math.Max(1, (p.UsaPlaca ? pisoDaPlaca : 0) + p.ParteDaAvaliaCents);

    /// <summary>
    /// O custo da placa da revenda: o maior entre as placas que ela tem em
    /// estoque (o que pagou no pacote); sem estoque, o preço por placa do
    /// pacote mais caro, que é o maior custo possível.
    /// </summary>
    public async Task<long> PisoDaPlaca(CancellationToken ct)
    {
        var doEstoque = await banco.Placas.Where(p => p.Situacao == SituacaoDaPlaca.EmEstoque).MaxAsync(p => (long?)p.CustoCents, ct);
        if (doEstoque is > 0)
        {
            return doEstoque.Value;
        }

        var pacotes = await banco.Pacotes.AsNoTracking().Where(p => p.Ativo).ToListAsync(ct);
        return pacotes.Count == 0 ? 1 : Math.Max(1, pacotes.Max(p => p.CustoPorPlacaCents));
    }

    /// <summary>A linha da tabela da revenda para o produto: a que existe, ou uma nova com o preço sugerido.</summary>
    private async Task<(Produto Produto, ProdutoDaPlataforma? DoCatalogo)> Garantir(Guid id, CancellationToken ct)
    {
        var doCatalogo = await banco.ProdutosDaPlataforma.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
        if (doCatalogo is null)
        {
            var antigo = await banco.Produtos.SingleOrDefaultAsync(p => p.Id == id && p.ProdutoDaPlataformaId == null, ct)
                ?? throw new RegraDeNegocioException("Produto não encontrado.");
            return (antigo, null);
        }

        var produto = await banco.Produtos.SingleOrDefaultAsync(p => p.ProdutoDaPlataformaId == id, ct);
        if (produto is null)
        {
            produto = Produto.DaPlataforma(doCatalogo);
            banco.Produtos.Add(produto);
        }

        return (produto, doCatalogo);
    }

    public static string? FotoUrl(ProdutoDaPlataforma p) =>
        p.Foto is not null ? $"/api/catalogo/{p.Id}/foto?v={p.FotoVersao}" : p.FotoPadrao;

    private static string Resumo(Produto p) =>
        $"{p.PrecoCents} centavos, comissão {p.ComissaoBps?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "padrão"} bps";
}
