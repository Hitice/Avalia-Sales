using AvaliaSales.Application.Equipe;
using AvaliaSales.Domain.Auditoria;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Auditoria;

public sealed record RegistroDaOperadoraDto(Guid Id, string Acao, string? Referencia, string? Corpo, string Quem, DateTimeOffset OcorridoEm);

/// <summary>
/// Quem da operadora mudou o quê na plataforma. O registro sai do middleware do
/// grupo /operadora, uma linha por escrita que deu certo; aqui só a leitura e a
/// exportação, para a própria operadora.
/// </summary>
public sealed class AuditoriaDaOperadora(IBancoDeDados banco, IContas contas)
{
    public async Task<Pagina<RegistroDaOperadoraDto>> Listar(string? busca, PedidoDePagina pedido, CancellationToken ct)
    {
        var pagina = await Filtrar(busca).OrderByDescending(r => r.OcorridoEm).ThenByDescending(r => r.Id).PaginarAsync(pedido, ct);
        return new Pagina<RegistroDaOperadoraDto>(await ParaDto(pagina.Itens, ct), pagina.Total, pagina.Proxima);
    }

    public async Task<IReadOnlyList<RegistroDaOperadoraDto>> Exportar(string? busca, CancellationToken ct) =>
        await ParaDto(await Filtrar(busca).OrderByDescending(r => r.OcorridoEm).ThenByDescending(r => r.Id).Take(Auditoria.ExportadosNoMaximo).ToListAsync(ct), ct);

    /// <summary>A busca acha pela rota ou pelo alvo: "despacho", "cupons", o id do pedido.</summary>
    private IQueryable<RegistroDaOperadora> Filtrar(string? busca)
    {
        var consulta = banco.AuditoriaDaOperadora.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(busca))
        {
            var termo = busca.Trim().ToLowerInvariant();
            // Vira lower(...) LIKE no Postgres: a cultura do .NET não entra na consulta.
#pragma warning disable CA1304, CA1311, CA1862
            consulta = consulta.Where(r => r.Acao.ToLower().Contains(termo) || (r.Referencia != null && r.Referencia.ToLower().Contains(termo)));
#pragma warning restore CA1304, CA1311, CA1862
        }

        return consulta;
    }

    private async Task<List<RegistroDaOperadoraDto>> ParaDto(IReadOnlyList<RegistroDaOperadora> registros, CancellationToken ct)
    {
        var nomes = await contas.Nomes(registros.Select(r => r.UsuarioId), ct);
        return registros.Select(r => new RegistroDaOperadoraDto(r.Id, r.Acao, r.Referencia, r.Corpo,
            nomes.GetValueOrDefault(r.UsuarioId, "Usuário excluído"), r.OcorridoEm)).ToList();
    }
}
