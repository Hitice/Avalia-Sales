using AvaliaSales.Domain;
using AvaliaSales.Domain.Pedidos;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Pedidos;

public sealed record PacoteDto(Guid Id, int Quantidade, long PrecoCents, long CustoPorPlacaCents, bool Ativo);

public sealed record GravarPacote(int Quantidade, long PrecoCents);

/// <summary>Os pacotes que a operadora oferece. O revendedor só vê os ativos.</summary>
public sealed class Pacotes(IBancoDeDados banco)
{
    public async Task<IReadOnlyList<PacoteDto>> Listar(bool soAtivos, CancellationToken ct) =>
        await banco.Pacotes.AsNoTracking()
            .Where(p => !soAtivos || p.Ativo)
            .OrderBy(p => p.Quantidade)
            .Select(p => new PacoteDto(p.Id, p.Quantidade, p.PrecoCents, p.PrecoCents / p.Quantidade, p.Ativo))
            .ToListAsync(ct);

    public async Task<PacoteDto> Criar(GravarPacote comando, CancellationToken ct)
    {
        await ExigirQuantidadeLivre(comando.Quantidade, null, ct);
        var pacote = new Pacote(comando.Quantidade, comando.PrecoCents);
        banco.Pacotes.Add(pacote);
        await banco.SaveChangesAsync(ct);
        return ParaDto(pacote);
    }

    public async Task<PacoteDto> Alterar(Guid id, GravarPacote comando, CancellationToken ct)
    {
        var pacote = await Achar(id, ct);
        await ExigirQuantidadeLivre(comando.Quantidade, id, ct);
        pacote.Alterar(comando.Quantidade, comando.PrecoCents);
        await banco.SaveChangesAsync(ct);
        return ParaDto(pacote);
    }

    public async Task DefinirAtivo(Guid id, bool ativo, CancellationToken ct)
    {
        (await Achar(id, ct)).DefinirAtivo(ativo);
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>O pedido copiou quantidade e preço: excluir o pacote não muda pedido nenhum.</summary>
    public async Task Excluir(Guid id, CancellationToken ct)
    {
        var pacote = await Achar(id, ct);
        banco.Pacotes.Remove(pacote);
        await banco.SaveChangesAsync(ct);
    }

    private async Task ExigirQuantidadeLivre(int quantidade, Guid? exceto, CancellationToken ct)
    {
        if (await banco.Pacotes.AnyAsync(p => p.Quantidade == quantidade && p.Id != exceto, ct))
        {
            throw new RegraDeNegocioException($"Pacote de {quantidade} placas já cadastrado.");
        }
    }

    private async Task<Pacote> Achar(Guid id, CancellationToken ct) =>
        await banco.Pacotes.SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw new RegraDeNegocioException("Pacote não encontrado.");

    private static PacoteDto ParaDto(Pacote p) => new(p.Id, p.Quantidade, p.PrecoCents, p.CustoPorPlacaCents, p.Ativo);
}
