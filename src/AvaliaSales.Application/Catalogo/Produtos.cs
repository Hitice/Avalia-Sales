using AvaliaSales.Domain.Auditoria;
using AvaliaSales.Domain.Catalogo;
using AvaliaSales.Domain;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Catalogo;

public sealed record ProdutoDto(Guid Id, string Nome, long PrecoCents, bool Ativo, int? ComissaoBps);

/// <summary>Sem comissão, o produto segue a do vendedor ou a padrão.</summary>
public sealed record CadastrarProduto(string Nome, long PrecoCents, int? ComissaoBps = null);

/// <summary>
/// Catálogo do revendedor logado. O filtro por revendedor fica na
/// infraestrutura; aqui não há parâmetro de revendedor para alguém esquecer.
/// </summary>
public sealed class Produtos(IBancoDeDados banco, Auditoria.Auditoria auditoria)
{
    public async Task<IReadOnlyList<ProdutoDto>> Listar(CancellationToken ct) =>
        await banco.Produtos
            .AsNoTracking()
            .OrderBy(p => p.Nome)
            .Select(p => new ProdutoDto(p.Id, p.Nome, p.PrecoCents, p.Ativo, p.ComissaoBps))
            .ToListAsync(ct);

    public async Task<ProdutoDto> Cadastrar(CadastrarProduto comando, CancellationToken ct)
    {
        var produto = new Produto(comando.Nome, comando.PrecoCents, comando.ComissaoBps);
        banco.Produtos.Add(produto);
        await banco.SaveChangesAsync(ct);
        return new ProdutoDto(produto.Id, produto.Nome, produto.PrecoCents, produto.Ativo, produto.ComissaoBps);
    }

    public async Task<ProdutoDto> Alterar(Guid id, CadastrarProduto comando, CancellationToken ct)
    {
        var produto = await Achar(id, ct);
        var antes = $"{produto.PrecoCents} centavos, comissão {produto.ComissaoBps?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "padrão"} bps";
        produto.Alterar(comando.Nome, comando.PrecoCents, comando.ComissaoBps);
        var depois = $"{produto.PrecoCents} centavos, comissão {produto.ComissaoBps?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "padrão"} bps";
        if (depois != antes)
        {
            auditoria.Registrar(AcaoAuditada.ProdutoAlterado, $"Produto {produto.Nome}", produto.Id.ToString(), antes, depois);
        }

        await banco.SaveChangesAsync(ct);
        return new ProdutoDto(produto.Id, produto.Nome, produto.PrecoCents, produto.Ativo, produto.ComissaoBps);
    }

    public async Task DefinirAtivo(Guid id, bool ativo, CancellationToken ct)
    {
        (await Achar(id, ct)).DefinirAtivo(ativo);
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>Produto com venda é desativado, e não excluído: a venda continua explicável.</summary>
    public async Task Excluir(Guid id, CancellationToken ct)
    {
        var produto = await Achar(id, ct);
        if (await banco.Vendas.AnyAsync(v => v.ProdutoId == id, ct))
        {
            throw new RegraDeNegocioException("Produto com venda é desativado, e não excluído.");
        }

        banco.Produtos.Remove(produto);
        await banco.SaveChangesAsync(ct);
    }

    private async Task<Produto> Achar(Guid id, CancellationToken ct) =>
        await banco.Produtos.SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw new RegraDeNegocioException("Produto não encontrado.");
}
