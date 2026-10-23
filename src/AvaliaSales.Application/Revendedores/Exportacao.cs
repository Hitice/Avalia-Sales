using AvaliaSales.Domain.Financeiro;
using AvaliaSales.Domain.Placas;
using AvaliaSales.Domain.Vendas;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Revendedores;

public sealed record ClienteExportado(string Nome, string Responsavel, string Telefone, string? Documento, string? Email);

public sealed record VendaExportada(DateTimeOffset VendidaEm, string Cliente, string? Placa, long ValorCents, string Meio, SituacaoDaVenda Situacao);

public sealed record PlacaExportada(string Codigo, SituacaoDaPlaca Situacao, DateTimeOffset? VendidaEm, DateTimeOffset? VenceEm, string? Destino);

public sealed record LancamentoExportado(DateTimeOffset OcorridoEm, TipoDeLancamento Tipo, long ValorCents, string Descricao);

public sealed record DadosExportados(DateTimeOffset GeradoEm, string Revenda, string? Cnpj, IReadOnlyList<ClienteExportado> Clientes,
    IReadOnlyList<VendaExportada> Vendas, IReadOnlyList<PlacaExportada> Placas, IReadOnlyList<LancamentoExportado> Razao);

/// <summary>
/// Os dados da revenda num arquivo só, para o dono guardar: o recurso de exportação
/// do distrato (cláusula 3.3). O RLS já limita tudo à revenda da sessão; os
/// clientes que passaram para a Avalia não aparecem mais (cláusula 3.2).
/// </summary>
public sealed class Exportacao(IBancoDeDados banco, IRevendedorAtual atual, TimeProvider relogio)
{
    public async Task<DadosExportados> Gerar(CancellationToken ct)
    {
        var revenda = await banco.Revendedores.AsNoTracking().Where(r => r.Id == atual.RevendedorId).Select(r => new { r.Nome, r.Cnpj }).SingleAsync(ct);
        var clientes = await banco.Clientes.AsNoTracking().OrderBy(c => c.Nome)
            .Select(c => new ClienteExportado(c.Nome, c.Responsavel, c.Telefone, c.Documento, c.Email)).ToListAsync(ct);
        var vendas = await (
            from v in banco.Vendas.AsNoTracking()
            join c in banco.Clientes.AsNoTracking() on v.ClienteId equals c.Id into cs
            from c in cs.DefaultIfEmpty()
            join p in banco.Placas.AsNoTracking() on v.PlacaId equals p.Id into ps
            from p in ps.DefaultIfEmpty()
            orderby v.VendidaEm
            select new VendaExportada(v.VendidaEm, c != null ? c.Nome : "Cliente transferido", p != null ? p.Codigo : null, v.ValorCents, v.Meio.ToString(), v.Situacao))
            .ToListAsync(ct);
        var placas = await banco.Placas.AsNoTracking().OrderBy(p => p.Sequencia)
            .Select(p => new PlacaExportada(p.Codigo, p.Situacao, p.VendidaEm, p.VenceEm, p.Destino)).ToListAsync(ct);
        var razao = await banco.Lancamentos.AsNoTracking().OrderBy(l => l.OcorridoEm)
            .Select(l => new LancamentoExportado(l.OcorridoEm, l.Tipo, l.ValorCents, l.Descricao)).ToListAsync(ct);
        return new DadosExportados(relogio.GetUtcNow(), revenda.Nome, revenda.Cnpj, clientes, vendas, placas, razao);
    }
}
