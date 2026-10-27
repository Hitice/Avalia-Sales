using AvaliaSales.Domain;
using AvaliaSales.Domain.Pedidos;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Pedidos;

public sealed record CupomDto(Guid Id, string Codigo, int? DescontoBps, long? DescontoCents, DateTimeOffset? ValidoAte, int? UsosMaximos, int Usos, bool Ativo);

public sealed record GravarCupom(string Codigo, int? DescontoBps, long? DescontoCents, DateTimeOffset? ValidoAte, int? UsosMaximos);

/// <summary>Os cupons da operadora para o pedido de placas. Cupom usado é desativado, e não excluído.</summary>
public sealed class Cupons(IBancoDeDados banco)
{
    public async Task<Pagina<CupomDto>> Listar(string? busca, PedidoDePagina pedido, CancellationToken ct)
    {
        var consulta = banco.Cupons.AsNoTracking();
        var termo = Paginacao.Termo(busca).ToUpperInvariant();
        if (termo.Length > 0)
        {
            consulta = consulta.Where(c => c.Codigo.Contains(termo));
        }

        return (await consulta.OrderBy(c => c.Codigo).ThenBy(c => c.Id).PaginarAsync(pedido, ct)).Mapear(Dto);
    }

    public async Task<CupomDto> Criar(GravarCupom comando, CancellationToken ct)
    {
        var cupom = new Cupom(comando.Codigo, comando.DescontoBps, comando.DescontoCents, comando.ValidoAte, comando.UsosMaximos);
        if (await banco.Cupons.AnyAsync(c => c.Codigo == cupom.Codigo, ct))
        {
            throw new RegraDeNegocioException($"Cupom {cupom.Codigo} já cadastrado.");
        }

        banco.Cupons.Add(cupom);
        await banco.SaveChangesAsync(ct);
        return Dto(cupom);
    }

    public async Task<CupomDto> Alterar(Guid id, GravarCupom comando, CancellationToken ct)
    {
        var cupom = await Achar(id, ct);
        cupom.Alterar(comando.DescontoBps, comando.DescontoCents, comando.ValidoAte, comando.UsosMaximos);
        await banco.SaveChangesAsync(ct);
        return Dto(cupom);
    }

    public async Task DefinirAtivo(Guid id, bool ativo, CancellationToken ct)
    {
        (await Achar(id, ct)).DefinirAtivo(ativo);
        await banco.SaveChangesAsync(ct);
    }

    public async Task Excluir(Guid id, CancellationToken ct)
    {
        var cupom = await Achar(id, ct);
        if (cupom.Usos > 0)
        {
            throw new RegraDeNegocioException("Cupom já usado não se exclui. Desative.");
        }

        banco.Cupons.Remove(cupom);
        await banco.SaveChangesAsync(ct);
    }

    private async Task<Cupom> Achar(Guid id, CancellationToken ct) =>
        await banco.Cupons.SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw new RegraDeNegocioException("Cupom não encontrado.");

    private static CupomDto Dto(Cupom c) => new(c.Id, c.Codigo, c.DescontoBps, c.DescontoCents, c.ValidoAte, c.UsosMaximos, c.Usos, c.Ativo);
}
