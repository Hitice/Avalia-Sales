using AvaliaSales.Domain.Revendedores;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Revendedores;

/// <summary>A marca como o app aplica: as cores prontas, com o texto de cada uma já escolhido pelo contraste.</summary>
public sealed record MarcaDto(string? NomeExibido, string? CorPrimaria, string? TextoSobrePrimaria, string? CorDestaque, string? TextoSobreDestaque, bool TemLogo, long Versao);

public sealed record GravarMarca(string? NomeExibido, string CorPrimaria, string CorDestaque);

/// <summary>White label: o dono define a marca, e a equipe dele vê o app com ela. Login e área da operadora ficam com a marca da plataforma.</summary>
public sealed class Marca(IBancoDeDados banco, IRevendedorAtual atual, TimeProvider relogio)
{
    public async Task<MarcaDto> Ler(CancellationToken ct)
    {
        var r = await banco.Revendedores.AsNoTracking().Where(x => x.Id == atual.RevendedorId)
            .Select(x => new { x.NomeExibido, x.CorPrimaria, x.CorDestaque, TemLogo = banco.Logos.Any(l => l.RevendedorId == x.Id), x.MarcaAlteradaEm })
            .SingleAsync(ct);
        return new MarcaDto(r.NomeExibido, r.CorPrimaria, r.CorPrimaria is null ? null : Contraste.TextoSobre(r.CorPrimaria),
            r.CorDestaque, r.CorDestaque is null ? null : Contraste.TextoSobre(r.CorDestaque), r.TemLogo, r.MarcaAlteradaEm?.ToUnixTimeSeconds() ?? 0);
    }

    public async Task<MarcaDto> Gravar(GravarMarca comando, CancellationToken ct)
    {
        var revendedor = await Atual(ct);
        revendedor.DefinirMarca(comando.NomeExibido, comando.CorPrimaria, comando.CorDestaque, relogio.GetUtcNow());
        await banco.SaveChangesAsync(ct);
        return await Ler(ct);
    }

    /// <summary>Volta à marca da plataforma.</summary>
    public async Task Restaurar(CancellationToken ct)
    {
        var revendedor = await Atual(ct);
        revendedor.RemoverMarca(relogio.GetUtcNow());
        await banco.Logos.Where(l => l.RevendedorId == revendedor.Id).ExecuteDeleteAsync(ct);
        await banco.SaveChangesAsync(ct);
    }

    public async Task GravarLogo(byte[] conteudo, CancellationToken ct)
    {
        var revendedor = await Atual(ct);
        var logo = await banco.Logos.SingleOrDefaultAsync(l => l.RevendedorId == revendedor.Id, ct);
        if (logo is null)
        {
            banco.Logos.Add(new LogoDoRevendedor(revendedor.Id, conteudo));
        }
        else
        {
            logo.Trocar(conteudo);
        }

        revendedor.TocarMarca(relogio.GetUtcNow());
        await banco.SaveChangesAsync(ct);
    }

    public async Task RemoverLogo(CancellationToken ct)
    {
        var revendedor = await Atual(ct);
        await banco.Logos.Where(l => l.RevendedorId == revendedor.Id).ExecuteDeleteAsync(ct);
        revendedor.TocarMarca(relogio.GetUtcNow());
        await banco.SaveChangesAsync(ct);
    }

    public async Task<(byte[] Conteudo, string Tipo)?> Logo(CancellationToken ct)
    {
        var logo = await banco.Logos.AsNoTracking().SingleOrDefaultAsync(l => l.RevendedorId == atual.RevendedorId, ct);
        return logo is null ? null : (logo.Conteudo, logo.Tipo);
    }

    private Task<Revendedor> Atual(CancellationToken ct) => banco.Revendedores.SingleAsync(r => r.Id == atual.RevendedorId, ct);
}
