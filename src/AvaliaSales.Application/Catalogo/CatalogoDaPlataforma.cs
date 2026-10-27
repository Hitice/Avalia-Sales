using AvaliaSales.Domain;
using AvaliaSales.Domain.Catalogo;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Catalogo;

public sealed record ProdutoDaPlataformaDto(Guid Id, string Chave, string Nome, string Descricao, TipoDeProduto Tipo, long PrecoSugeridoCents,
    SituacaoDoProduto Situacao, int Ordem, string? FotoUrl, bool TemFotoEnviada, int DescontoDaRevendaBps, long ParteDaAvaliaCents, int QuantidadeMinima);

/// <summary>Sem desconto ou pedido mínimo informado, o produto novo nasce com o padrão e o que existe mantém o dele.</summary>
public sealed record GravarProdutoDaPlataforma(string Chave, string Nome, string? Descricao, TipoDeProduto Tipo, long PrecoSugeridoCents, int Ordem,
    int? DescontoDaRevendaBps = null, int? QuantidadeMinima = null);

public sealed record DefinirSituacaoDoProduto(SituacaoDoProduto Situacao);

/// <summary>
/// O catálogo da Avalia, cuidado pela operadora: o que existe, o preço
/// sugerido, a foto e se está à venda. Fora de linha some do catálogo das
/// revendas, mas a venda antiga continua apontando para ele.
/// </summary>
public sealed class CatalogoDaPlataforma(IBancoDeDados banco, TimeProvider relogio)
{
    public async Task<IReadOnlyList<ProdutoDaPlataformaDto>> Listar(CancellationToken ct) =>
        (await banco.ProdutosDaPlataforma.AsNoTracking().OrderBy(p => p.Ordem).ThenBy(p => p.Nome).ToListAsync(ct)).Select(Dto).ToList();

    public async Task<ProdutoDaPlataformaDto> Cadastrar(GravarProdutoDaPlataforma comando, CancellationToken ct)
    {
        var chave = (comando.Chave ?? "").Trim().ToLowerInvariant();
        if (await banco.ProdutosDaPlataforma.AnyAsync(p => p.Chave == chave, ct))
        {
            throw new RegraDeNegocioException("Chave já cadastrada.");
        }

        var produto = new ProdutoDaPlataforma(chave, comando.Nome, comando.Descricao ?? "", comando.Tipo, comando.PrecoSugeridoCents, comando.Ordem, relogio.GetUtcNow());
        produto.DefinirDescontoDaRevenda(comando.DescontoDaRevendaBps ?? ProdutoDaPlataforma.DescontoPadraoBps);
        produto.DefinirQuantidadeMinima(comando.QuantidadeMinima ?? 1);
        banco.ProdutosDaPlataforma.Add(produto);
        await banco.SaveChangesAsync(ct);
        return Dto(produto);
    }

    /// <summary>A chave não muda: é por ela que a foto padrão e as integrações acham o produto.</summary>
    public async Task<ProdutoDaPlataformaDto> Alterar(Guid id, GravarProdutoDaPlataforma comando, CancellationToken ct)
    {
        var produto = await Achar(id, ct);
        produto.Alterar(comando.Nome, comando.Descricao, comando.Tipo, comando.PrecoSugeridoCents, comando.Ordem);
        if (comando.DescontoDaRevendaBps is { } desconto)
        {
            produto.DefinirDescontoDaRevenda(desconto);
        }

        if (comando.QuantidadeMinima is { } minima)
        {
            produto.DefinirQuantidadeMinima(minima);
        }

        await banco.SaveChangesAsync(ct);
        return Dto(produto);
    }

    public async Task<ProdutoDaPlataformaDto> DefinirSituacao(Guid id, SituacaoDoProduto situacao, CancellationToken ct)
    {
        var produto = await Achar(id, ct);
        produto.DefinirSituacao(situacao);
        await banco.SaveChangesAsync(ct);
        return Dto(produto);
    }

    public async Task<ProdutoDaPlataformaDto> GravarFoto(Guid id, byte[] conteudo, string tipo, CancellationToken ct)
    {
        var produto = await Achar(id, ct);
        produto.DefinirFoto(conteudo, tipo);
        await banco.SaveChangesAsync(ct);
        return Dto(produto);
    }

    public async Task<ProdutoDaPlataformaDto> RemoverFoto(Guid id, CancellationToken ct)
    {
        var produto = await Achar(id, ct);
        produto.RemoverFoto();
        await banco.SaveChangesAsync(ct);
        return Dto(produto);
    }

    /// <summary>A foto enviada, para qualquer um ver: está no catálogo que a equipe mostra ao cliente.</summary>
    public async Task<(byte[] Conteudo, string Tipo)?> Foto(Guid id, CancellationToken ct)
    {
        var foto = await banco.ProdutosDaPlataforma.AsNoTracking().Where(p => p.Id == id && p.Foto != null)
            .Select(p => new { p.Foto, p.FotoTipo }).SingleOrDefaultAsync(ct);
        return foto is null ? null : (foto.Foto!, foto.FotoTipo!);
    }

    private async Task<ProdutoDaPlataforma> Achar(Guid id, CancellationToken ct) =>
        await banco.ProdutosDaPlataforma.SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw new RegraDeNegocioException("Produto não encontrado.");

    private static ProdutoDaPlataformaDto Dto(ProdutoDaPlataforma p) =>
        new(p.Id, p.Chave, p.Nome, p.Descricao, p.Tipo, p.PrecoSugeridoCents, p.Situacao, p.Ordem, Catalogo.FotoUrl(p), p.Foto is not null,
            p.DescontoDaRevendaBps, p.ParteDaAvaliaCents, p.QuantidadeMinima);
}
