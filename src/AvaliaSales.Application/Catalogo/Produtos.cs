using AvaliaSales.Domain;
using AvaliaSales.Domain.Catalogo;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Catalogo;

public sealed record ProdutoDto(Guid Id, string Nome, long PrecoCents, bool Ativo);

public sealed record CadastrarProduto(string Nome, long PrecoCents);

/// <summary>
/// Catálogo do revendedor logado. O filtro por revendedor fica na
/// infraestrutura; aqui não há parâmetro de revendedor para alguém esquecer.
/// </summary>
public sealed class Produtos(IBancoDeDados banco)
{
    public async Task<IReadOnlyList<ProdutoDto>> Listar(CancellationToken ct) =>
        await banco.Produtos
            .AsNoTracking()
            .OrderBy(p => p.Nome)
            .Select(p => new ProdutoDto(p.Id, p.Nome, p.PrecoCents, p.Ativo))
            .ToListAsync(ct);

    public async Task<ProdutoDto> Cadastrar(CadastrarProduto comando, CancellationToken ct)
    {
        var produto = new Produto(comando.Nome, comando.PrecoCents);
        banco.Produtos.Add(produto);
        await banco.SaveChangesAsync(ct);
        return new ProdutoDto(produto.Id, produto.Nome, produto.PrecoCents, produto.Ativo);
    }

    public async Task<ProdutoDto> Alterar(Guid id, CadastrarProduto comando, CancellationToken ct)
    {
        var produto = await Achar(id, ct);
        produto.Alterar(comando.Nome, comando.PrecoCents);
        await banco.SaveChangesAsync(ct);
        return new ProdutoDto(produto.Id, produto.Nome, produto.PrecoCents, produto.Ativo);
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
