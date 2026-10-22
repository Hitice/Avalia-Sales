using AvaliaSales.Application.Placas;
using AvaliaSales.Domain;
using AvaliaSales.Domain.Links;
using AvaliaSales.Domain.Placas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Application.Links;

public sealed record LinkDto(Guid Id, string Codigo, string Endereco, string Titulo, string Destino, Guid? ClienteId, bool Ativo, long Cliques,
    DateTimeOffset? UltimoCliqueEm, DateTimeOffset CriadoEm);

/// <summary>Título e destino; o destino vem como na venda (lugar do Google, link de avaliação colado ou link do cliente).</summary>
public sealed record GravarLink(string Titulo, DestinoInformado Destino);

public sealed record LeituraDoLink(string? Destino, bool Ativo);

/// <summary>
/// O encurtador do revendedor. A venda com destino no Google cria ou
/// reaproveita o link do negócio; a tela Links cria o do negócio sem placa.
/// </summary>
public sealed class Links(IBancoDeDados banco, IRevendedorAtual atual, IOptions<OpcoesDaPlataforma> plataforma, TimeProvider relogio)
{
    /// <summary>Sorteios antes de desistir; com 32^6 códigos, chegar aqui é defeito, e não azar.</summary>
    private const int Sorteios = 20;

    public async Task<IReadOnlyList<LinkDto>> Listar(CancellationToken ct) =>
        (await banco.Links.AsNoTracking().OrderByDescending(l => l.CriadoEm).ToListAsync(ct)).Select(ParaDto).ToList();

    public async Task<LinkDto> Criar(GravarLink comando, CancellationToken ct)
    {
        var destino = comando.Destino.Resolver(plataforma.Value.HostPublico);
        if (await banco.Links.AsNoTracking().FirstOrDefaultAsync(l => l.Destino == destino, ct) is { } existente)
        {
            throw new RegraDeNegocioException($"Este destino já tem o link {plataforma.Value.EnderecoDoLink(existente.Codigo)}.");
        }

        var link = await Novo(comando.Titulo, destino, null, ct);
        await banco.SaveChangesAsync(ct);
        return ParaDto(link);
    }

    public async Task<LinkDto> Alterar(Guid id, GravarLink comando, CancellationToken ct)
    {
        var link = await Achar(id, ct);
        var destino = comando.Destino.Resolver(plataforma.Value.HostPublico);
        if (await banco.Links.AnyAsync(l => l.Id != id && l.Destino == destino, ct))
        {
            throw new RegraDeNegocioException("Outro link já leva a este destino.");
        }

        link.Alterar(comando.Titulo, destino);
        await banco.SaveChangesAsync(ct);
        return ParaDto(link);
    }

    public async Task DefinirAtivo(Guid id, bool ativo, CancellationToken ct)
    {
        (await Achar(id, ct)).DefinirAtivo(ativo);
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>O código continua reservado: o endereço excluído responde "não encontrado" para sempre.</summary>
    public async Task Excluir(Guid id, CancellationToken ct)
    {
        banco.Links.Remove(await Achar(id, ct));
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>
    /// O link de avaliação do negócio vendido: o mesmo destino reaproveita o
    /// link que já existe. Grava junto com a venda, na transação de quem chama.
    /// </summary>
    public async Task<string> DoNegocio(Guid clienteId, string empresa, string destino, CancellationToken ct)
    {
        var link = await banco.Links.FirstOrDefaultAsync(l => l.Destino == destino, ct)
            ?? await Novo(Titulo($"Avaliação no Google - {empresa}"), destino, clienteId, ct);
        return plataforma.Value.EnderecoDoLink(link.Codigo);
    }

    /// <summary>O clique, dentro do revendedor dono do código. HEAD e robô não contam.</summary>
    public async Task<LeituraDoLink?> Ler(string codigo, bool contar, CancellationToken ct)
    {
        var link = await banco.Links.AsNoTracking().Where(l => l.Codigo == codigo).Select(l => new { l.Id, l.Destino, l.Ativo }).SingleOrDefaultAsync(ct);
        if (link is null)
        {
            return null;
        }

        if (contar && link.Ativo)
        {
            var agora = relogio.GetUtcNow();
            await banco.Links.Where(l => l.Id == link.Id)
                .ExecuteUpdateAsync(l => l.SetProperty(x => x.Cliques, x => x.Cliques + 1).SetProperty(x => x.UltimoCliqueEm, agora), ct);
        }

        return new LeituraDoLink(link.Destino, link.Ativo);
    }

    private async Task<LinkCurto> Novo(string titulo, string destino, Guid? clienteId, CancellationToken ct)
    {
        var revendedorId = atual.RevendedorId ?? throw new InvalidOperationException("Link sem revendedor.");
        for (var i = 0; i < Sorteios; i++)
        {
            var codigo = CodigoDaPlaca.Sortear();
            if (await banco.CodigosDeLink.AnyAsync(c => c.Codigo == codigo, ct)
                || banco.CodigosDeLink.Local.Any(c => c.Codigo == codigo))
            {
                continue;
            }

            banco.CodigosDeLink.Add(new CodigoDeLink(codigo, revendedorId));
            var link = new LinkCurto(codigo, titulo, destino, clienteId, relogio.GetUtcNow());
            banco.Links.Add(link);
            return link;
        }

        throw new InvalidOperationException($"Nenhum código livre em {Sorteios} sorteios.");
    }

    private static string Titulo(string texto) => texto.Length > LinkCurto.TituloMaximo ? texto[..LinkCurto.TituloMaximo] : texto;

    private async Task<LinkCurto> Achar(Guid id, CancellationToken ct) =>
        await banco.Links.SingleOrDefaultAsync(l => l.Id == id, ct) ?? throw new RegraDeNegocioException("Link não encontrado.");

    private LinkDto ParaDto(LinkCurto l) =>
        new(l.Id, l.Codigo, plataforma.Value.EnderecoDoLink(l.Codigo), l.Titulo, l.Destino, l.ClienteId, l.Ativo, l.Cliques, l.UltimoCliqueEm, l.CriadoEm);
}
