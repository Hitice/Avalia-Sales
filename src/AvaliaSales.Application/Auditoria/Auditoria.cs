using AvaliaSales.Application.Equipe;
using AvaliaSales.Domain.Auditoria;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Auditoria;

public sealed record RegistroDto(Guid Id, AcaoAuditada Acao, string Alvo, string Referencia, string? Antes, string? Depois, string Quem, DateTimeOffset OcorridoEm);

public sealed record PaginaDeAuditoria(IReadOnlyList<RegistroDto> Registros, int Total);

/// <summary>
/// Registra na transação de quem chama (o SaveChanges é dele) e lista para o
/// dono. Dinheiro e comissão continuam no razão; aqui fica a decisão de quem.
/// </summary>
public sealed class Auditoria(IBancoDeDados banco, IUsuarioAtual usuario, IContas contas, TimeProvider relogio)
{
    public const int PorPagina = 20;

    /// <summary>Teto da exportação: acima disso, filtre por código ou por ação.</summary>
    public const int ExportadosNoMaximo = 10_000;

    public void Registrar(AcaoAuditada acao, string alvo, string referencia, string? antes = null, string? depois = null) =>
        banco.Auditoria.Add(new RegistroDeAuditoria(usuario.UsuarioId, acao, alvo, referencia, antes, depois, relogio.GetUtcNow()));

    /// <summary>Com <paramref name="referencia"/>, só o que tocou aquele alvo (a ficha da placa usa assim); com <paramref name="acao"/>, só aquela ação.</summary>
    public async Task<PaginaDeAuditoria> Listar(string? referencia, AcaoAuditada? acao, int pagina, CancellationToken ct)
    {
        var consulta = Filtrar(referencia, acao);
        var total = await consulta.CountAsync(ct);
        var registros = await consulta.OrderByDescending(r => r.OcorridoEm)
            .Skip((Math.Max(pagina, 1) - 1) * PorPagina).Take(PorPagina).ToListAsync(ct);
        return new PaginaDeAuditoria(await ParaDto(registros, ct), total);
    }

    /// <summary>Tudo o que o filtro pega, do mais novo ao mais antigo, até o teto, para o dono guardar ou entregar.</summary>
    public async Task<IReadOnlyList<RegistroDto>> Exportar(string? referencia, AcaoAuditada? acao, CancellationToken ct)
    {
        var registros = await Filtrar(referencia, acao).OrderByDescending(r => r.OcorridoEm).Take(ExportadosNoMaximo).ToListAsync(ct);
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

    private async Task<List<RegistroDto>> ParaDto(List<RegistroDeAuditoria> registros, CancellationToken ct)
    {
        var nomes = await contas.Nomes(registros.Where(r => r.UsuarioId != null).Select(r => r.UsuarioId!.Value), ct);
        return registros.Select(r => new RegistroDto(r.Id, r.Acao, r.Alvo, r.Referencia, r.Antes, r.Depois,
            r.UsuarioId is { } id ? nomes.GetValueOrDefault(id, "Usuário excluído") : "Sistema", r.OcorridoEm)).ToList();
    }
}
