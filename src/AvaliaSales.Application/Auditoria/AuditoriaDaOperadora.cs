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
    public async Task<Pagina<RegistroDaOperadoraDto>> Listar(string? busca, PedidoDePagina pedido, CancellationToken ct, string? mes = null)
    {
        var pagina = await Filtrar(busca, mes).OrderByDescending(r => r.OcorridoEm).ThenByDescending(r => r.Id).PaginarAsync(pedido, ct);
        return new Pagina<RegistroDaOperadoraDto>(await ParaDto(pagina.Itens, ct), pagina.Total, pagina.Proxima);
    }

    public async Task<IReadOnlyList<RegistroDaOperadoraDto>> Exportar(string? busca, CancellationToken ct, string? mes = null) =>
        await ParaDto(await Filtrar(busca, mes).OrderByDescending(r => r.OcorridoEm).ThenByDescending(r => r.Id).Take(Auditoria.ExportadosNoMaximo).ToListAsync(ct), ct);

    /// <summary>A busca acha pela rota ou pelo alvo: "despacho", "cupons", o id do pedido. O mês soma à busca.</summary>
    private IQueryable<RegistroDaOperadora> Filtrar(string? busca, string? mes)
    {
        var consulta = banco.AuditoriaDaOperadora.AsNoTracking();
        if (MesDoFiltro.Intervalo(mes) is var (inicio, fim))
        {
            consulta = consulta.Where(r => r.OcorridoEm >= inicio && r.OcorridoEm < fim);
        }

        if (!string.IsNullOrWhiteSpace(busca))
        {
            var termo = Paginacao.Termo(busca);
            consulta = consulta.Where(r => Busca.Normalizar(r.Acao).Contains(Busca.Normalizar(termo)) || (r.Referencia != null && Busca.Normalizar(r.Referencia).Contains(Busca.Normalizar(termo))));
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
