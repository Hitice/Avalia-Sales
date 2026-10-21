using AvaliaSales.Domain;
using AvaliaSales.Domain.Acesso;
using AvaliaSales.Domain.Equipe;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Equipe;

public sealed record VendedorDto(Guid Id, string Nome, string Email, string? Telefone, string? ChavePix, int? ComissaoBps, int ComissaoEfetivaBps, bool Ativo, int Vendas);

public sealed record NovoVendedor(string Nome, string Email, string Senha, string? Telefone, string? ChavePix, int? ComissaoBps);

public sealed record AlterarVendedor(string Nome, string? Telefone, string? ChavePix, int? ComissaoBps);

/// <summary>A equipe do revendedor: só o dono mexe, e toda mudança de comissão vale para venda nova.</summary>
public sealed class Equipe(IBancoDeDados banco, IContas contas, IRevendedorAtual atual, TimeProvider relogio)
{
    public async Task<IReadOnlyList<VendedorDto>> Listar(CancellationToken ct)
    {
        var padrao = await ComissaoPadrao(ct);
        var vendas = await banco.Vendas.AsNoTracking().GroupBy(v => v.VendedorId)
            .Select(g => new { g.Key, Total = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Total, ct);
        var vendedores = await banco.Vendedores.AsNoTracking().OrderBy(v => v.Nome).ToListAsync(ct);
        return vendedores.Select(v => ParaDto(v, padrao, vendas.GetValueOrDefault(v.Id))).ToList();
    }

    public async Task<VendedorDto> Criar(NovoVendedor comando, CancellationToken ct)
    {
        var revendedorId = atual.RevendedorId ?? throw new InvalidOperationException("Equipe sem revendedor.");
        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        var id = await contas.Criar(comando.Nome, comando.Email, comando.Senha, Papel.Vendedor, revendedorId, ct);
        var vendedor = new Vendedor(id, comando.Nome, comando.Email, relogio.GetUtcNow());
        vendedor.Alterar(comando.Nome, comando.Telefone, comando.ChavePix, comando.ComissaoBps);
        banco.Vendedores.Add(vendedor);
        await banco.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
        return ParaDto(vendedor, await ComissaoPadrao(ct), 0);
    }

    public async Task<VendedorDto> Alterar(Guid id, AlterarVendedor comando, CancellationToken ct)
    {
        var vendedor = await Achar(id, ct);
        vendedor.Alterar(comando.Nome, comando.Telefone, comando.ChavePix, comando.ComissaoBps);
        await banco.SaveChangesAsync(ct);
        return ParaDto(vendedor, await ComissaoPadrao(ct), await banco.Vendas.CountAsync(v => v.VendedorId == id, ct));
    }

    public async Task DefinirAtivo(Guid id, bool ativo, CancellationToken ct)
    {
        var vendedor = await Achar(id, ct);
        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        vendedor.DefinirAtivo(ativo);
        await banco.SaveChangesAsync(ct);
        await contas.DefinirAtivo(id, ativo, ct);
        await transacao.CommitAsync(ct);
    }

    /// <summary>Vendedor com venda é desativado, e não excluído: a venda continua com o nome dele.</summary>
    public async Task Excluir(Guid id, CancellationToken ct)
    {
        var vendedor = await Achar(id, ct);
        if (await banco.Vendas.AnyAsync(v => v.VendedorId == id, ct))
        {
            throw new RegraDeNegocioException("Vendedor com venda é desativado, e não excluído.");
        }

        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        foreach (var placa in await banco.Placas.Where(p => p.ConsignadaA == id).ToListAsync(ct))
        {
            placa.Devolver();
        }

        banco.Vendedores.Remove(vendedor);
        await banco.SaveChangesAsync(ct);
        await contas.Excluir(id, ct);
        await transacao.CommitAsync(ct);
    }

    public async Task<int> ComissaoPadrao(CancellationToken ct) =>
        await banco.Revendedores.Where(r => r.Id == atual.RevendedorId).Select(r => r.ComissaoPadraoBps).SingleAsync(ct);

    public async Task DefinirComissaoPadrao(int bps, CancellationToken ct)
    {
        var revendedor = await banco.Revendedores.SingleAsync(r => r.Id == atual.RevendedorId, ct);
        revendedor.DefinirComissaoPadrao(bps);
        await banco.SaveChangesAsync(ct);
    }

    private async Task<Vendedor> Achar(Guid id, CancellationToken ct) =>
        await banco.Vendedores.SingleOrDefaultAsync(v => v.Id == id, ct)
        ?? throw new RegraDeNegocioException("Vendedor não encontrado.");

    private static VendedorDto ParaDto(Vendedor v, int padrao, int vendas) =>
        new(v.Id, v.Nome, v.Email, v.Telefone, v.ChavePix, v.ComissaoBps, v.ComissaoBps ?? padrao, v.Ativo, vendas);
}
