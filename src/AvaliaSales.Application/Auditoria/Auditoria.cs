using AvaliaSales.Application.Equipe;
using AvaliaSales.Domain.Auditoria;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Auditoria;

public sealed record RegistroDto(Guid Id, AcaoAuditada Acao, string Alvo, string Referencia, string? Antes, string? Depois, string Quem, DateTimeOffset OcorridoEm);

/// <summary>
/// Registra na transação de quem chama (o SaveChanges é dele) e lista para o
/// dono. Dinheiro e comissão continuam no razão; aqui fica a decisão de quem.
/// </summary>
public sealed class Auditoria(IBancoDeDados banco, IUsuarioAtual usuario, IContas contas, TimeProvider relogio)
{
    /// <summary>Teto da exportação: acima disso, filtre por código ou por ação.</summary>
    public const int ExportadosNoMaximo = 10_000;

    public void Registrar(AcaoAuditada acao, string alvo, string referencia, string? antes = null, string? depois = null) =>
        banco.Auditoria.Add(new RegistroDeAuditoria(usuario.UsuarioId, acao, alvo, referencia, antes, depois, relogio.GetUtcNow()));

    /// <summary>
    /// Com <paramref name="referencia"/>, só o que tocou aquele alvo (a ficha da placa usa assim); com
    /// <paramref name="acao"/>, só aquela ação; com <paramref name="busca"/>, o texto no alvo, no código,
    /// no antes e no depois, no nome da ação ou no nome do vendedor que fez.
    /// </summary>
    public async Task<Pagina<RegistroDto>> Listar(string? referencia, AcaoAuditada? acao, PedidoDePagina pedido, CancellationToken ct, string? busca = null)
    {
        var consulta = await Buscar(Filtrar(referencia, acao), busca, ct);
        var pagina = await consulta.OrderByDescending(r => r.OcorridoEm).ThenByDescending(r => r.Id).PaginarAsync(pedido, ct);
        return new Pagina<RegistroDto>(await ParaDto(pagina.Itens, ct), pagina.Total, pagina.Proxima);
    }

    /// <summary>Tudo o que o filtro pega, do mais novo ao mais antigo, até o teto, para o dono guardar ou entregar.</summary>
    public async Task<IReadOnlyList<RegistroDto>> Exportar(string? referencia, AcaoAuditada? acao, CancellationToken ct, string? busca = null)
    {
        var registros = await (await Buscar(Filtrar(referencia, acao), busca, ct)).OrderByDescending(r => r.OcorridoEm).ThenByDescending(r => r.Id).Take(ExportadosNoMaximo).ToListAsync(ct);
        return await ParaDto(registros, ct);
    }

    private IQueryable<RegistroDeAuditoria> Filtrar(string? referencia, AcaoAuditada? acao)
    {
        var consulta = banco.Auditoria.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(referencia))
        {
            consulta = consulta.Where(r => r.Referencia == referencia.Trim());
        }

        if (acao is { } escolhida)
        {
            consulta = consulta.Where(r => r.Acao == escolhida);
        }

        return consulta;
    }

    private async Task<IQueryable<RegistroDeAuditoria>> Buscar(IQueryable<RegistroDeAuditoria> consulta, string? busca, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(busca))
        {
            return consulta;
        }

        var termo = Paginacao.Termo(busca);
        var semEspaco = termo.Replace(" ", string.Empty, StringComparison.Ordinal);
        var acoes = Enum.GetValues<AcaoAuditada>().Where(a => a.ToString().ToLowerInvariant().Contains(semEspaco, StringComparison.Ordinal)).ToList();
        var quem = await banco.Vendedores.AsNoTracking().Where(v => Busca.Normalizar(v.Nome).Contains(Busca.Normalizar(termo))).Select(v => (Guid?)v.Id).ToListAsync(ct);
        return consulta.Where(r => Busca.Normalizar(r.Alvo).Contains(Busca.Normalizar(termo)) || Busca.Normalizar(r.Referencia).Contains(Busca.Normalizar(termo))
            || (r.Antes != null && Busca.Normalizar(r.Antes).Contains(Busca.Normalizar(termo))) || (r.Depois != null && Busca.Normalizar(r.Depois).Contains(Busca.Normalizar(termo)))
            || acoes.Contains(r.Acao) || quem.Contains(r.UsuarioId));
    }

    private async Task<List<RegistroDto>> ParaDto(IReadOnlyList<RegistroDeAuditoria> registros, CancellationToken ct)
    {
        var nomes = await contas.Nomes(registros.Where(r => r.UsuarioId != null).Select(r => r.UsuarioId!.Value), ct);
        return registros.Select(r => new RegistroDto(r.Id, r.Acao, r.Alvo, r.Referencia, r.Antes, r.Depois,
            r.UsuarioId is { } id ? nomes.GetValueOrDefault(id, "Usuário excluído") : "Sistema", r.OcorridoEm)).ToList();
    }
}
