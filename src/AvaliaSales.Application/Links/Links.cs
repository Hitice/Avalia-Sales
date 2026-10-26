using AvaliaSales.Application.Placas;
using AvaliaSales.Domain.Acesso;
using AvaliaSales.Domain.Auditoria;
using AvaliaSales.Domain.Links;
using AvaliaSales.Domain.Placas;
using AvaliaSales.Domain;
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
public sealed class Links(IBancoDeDados banco, IRevendedorAtual atual, IUsuarioAtual usuario, Auditoria.Auditoria auditoria, IOptions<OpcoesDaPlataforma> plataforma, TimeProvider relogio,
    Revendedores.LimiteDeDestinos limiteDeDestinos, DestinosBloqueados destinosBloqueados)
{
    /// <summary>Sorteios antes de desistir; com 32^6 códigos, chegar aqui é defeito, e não azar.</summary>
    private const int Sorteios = 20;

    /// <summary>
    /// Do mais novo ao mais antigo; a busca vale para o título, o código e o destino. Com <paramref name="soAtivos"/>, só os ligados.
    /// O dono vê todos; o vendedor, os links dos clientes das vendas dele e os que ele mesmo criou - a busca roda só dentro disso.
    /// </summary>
    public async Task<Pagina<LinkDto>> Listar(string? busca, bool soAtivos, PedidoDePagina pedido, CancellationToken ct)
    {
        var consulta = DoUsuario(banco.Links.AsNoTracking());
        if (soAtivos)
        {
            consulta = consulta.Where(l => l.Ativo);
        }

        var termo = Paginacao.Termo(busca);
        if (termo.Length > 0)
        {
            consulta = consulta.Where(l => Busca.Normalizar(l.Titulo).Contains(Busca.Normalizar(termo)) || Busca.Normalizar(l.Codigo).Contains(Busca.Normalizar(termo)) || Busca.Normalizar(l.Destino).Contains(Busca.Normalizar(termo)));
        }

        return (await consulta.OrderByDescending(l => l.CriadoEm).ThenByDescending(l => l.Id).PaginarAsync(pedido, ct)).Mapear(ParaDto);
    }

    private IQueryable<LinkCurto> DoUsuario(IQueryable<LinkCurto> links)
    {
        if (usuario.Papel != Papel.Vendedor)
        {
            return links;
        }

        var eu = usuario.UsuarioId;
        var meusClientes = banco.Vendas.Where(v => v.VendedorId == eu).Select(v => (Guid?)v.ClienteId);
        var meusCodigos = banco.Auditoria.Where(a => a.Acao == AcaoAuditada.LinkCriado && a.UsuarioId == eu).Select(a => a.Referencia);
        return links.Where(l => (l.ClienteId != null && meusClientes.Contains(l.ClienteId)) || meusCodigos.Contains(l.Codigo));
    }

    public async Task<LinkDto> Criar(GravarLink comando, CancellationToken ct)
    {
        var destino = (comando.Destino ?? throw new RegraDeNegocioException("Escolha para onde o link leva.")).Resolver(plataforma.Value.HostPublico);
        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        await banco.TravarLinksDaRevenda(RevendedorDoToken(), ct);
        if (await banco.Links.AsNoTracking().FirstOrDefaultAsync(l => l.Destino == destino, ct) is { } existente)
        {
            // O vendedor só recebe o endereço do link que já enxerga; o de outro fica com o dono.
            throw new RegraDeNegocioException(await DoUsuario(banco.Links.AsNoTracking()).AnyAsync(l => l.Id == existente.Id, ct)
                ? $"Este destino já tem o link {plataforma.Value.EnderecoDoLink(existente.Codigo)}."
                : "Este destino já tem um link. Peça ao revendedor.");
        }

        await destinosBloqueados.ExigirPermitido(destino, ct);
        await limiteDeDestinos.Exigir(1, ct);
        var link = await Novo(comando.Titulo, destino, null, ct);
        auditoria.Registrar(AcaoAuditada.LinkCriado, $"Link {link.Codigo}", link.Codigo, null, destino);
        await banco.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
        return ParaDto(link);
    }

    public async Task<LinkDto> Alterar(Guid id, GravarLink comando, CancellationToken ct)
    {
        var link = await Achar(id, ct);
        var destino = (comando.Destino ?? throw new RegraDeNegocioException("Escolha para onde o link leva.")).Resolver(plataforma.Value.HostPublico);
        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        await banco.TravarLinksDaRevenda(RevendedorDoToken(), ct);
        if (await banco.Links.AnyAsync(l => l.Id != id && l.Destino == destino, ct))
        {
            throw new RegraDeNegocioException("Outro link já leva a este destino.");
        }

        var antes = link.Destino;
        if (antes != destino)
        {
            await destinosBloqueados.ExigirPermitido(destino, ct);
            await limiteDeDestinos.Exigir(1, ct);
        }

        link.Alterar(comando.Titulo, destino);
        if (antes != destino)
        {
            auditoria.Registrar(AcaoAuditada.LinkAlterado, $"Link {link.Codigo}", link.Codigo, antes, destino);
        }

        await banco.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
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
        var link = await Achar(id, ct);
        banco.Links.Remove(link);
        auditoria.Registrar(AcaoAuditada.LinkExcluido, $"Link {link.Codigo}", link.Codigo, link.Destino, null);
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>
    /// O link de avaliação do negócio vendido: o mesmo destino reaproveita o
    /// link que já existe. Grava junto com a venda, na transação de quem chama.
    /// </summary>
    public async Task<string> DoNegocio(Guid clienteId, string empresa, string destino, CancellationToken ct)
    {
        await banco.TravarLinksDaRevenda(RevendedorDoToken(), ct);
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

    private Guid RevendedorDoToken() => atual.RevendedorId ?? throw new InvalidOperationException("Link sem revendedor.");

    private static string Titulo(string texto) => texto.Length > LinkCurto.TituloMaximo ? texto[..LinkCurto.TituloMaximo] : texto;

    private async Task<LinkCurto> Achar(Guid id, CancellationToken ct) =>
        await banco.Links.SingleOrDefaultAsync(l => l.Id == id, ct) ?? throw new RegraDeNegocioException("Link não encontrado.");

    private LinkDto ParaDto(LinkCurto l) =>
        new(l.Id, l.Codigo, plataforma.Value.EnderecoDoLink(l.Codigo), l.Titulo, l.Destino, l.ClienteId, l.Ativo, l.Cliques, l.UltimoCliqueEm, l.CriadoEm);
}
