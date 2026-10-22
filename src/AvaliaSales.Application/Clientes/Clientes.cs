using AvaliaSales.Domain.Auditoria;
using AvaliaSales.Domain.Pagamentos;
using AvaliaSales.Domain;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Clientes;

public sealed record ClienteDaLista(Guid Id, string Nome, string Responsavel, string Telefone, string? Documento, string? Email, int Placas, DateTimeOffset CriadoEm, bool Anonimizado);

public sealed record VendaExportada(DateTimeOffset VendidaEm, string Placa, string? Destino, DateTimeOffset? VenceEm, long ValorCents, IReadOnlyList<CobrancaExportada> Cobrancas);

public sealed record CobrancaExportada(DateTimeOffset CriadaEm, string Meio, string Situacao, long ValorCents, DateTimeOffset? PagaEm);

/// <summary>Tudo o que a plataforma guarda sobre o cliente, no formato que ele pode levar (LGPD, art. 18, II e V).</summary>
public sealed record DadosDoCliente(
    DateTimeOffset GeradoEm, string Controlador, string Nome, string Responsavel, string Telefone, string? Documento, string? Email,
    string? LugarNoGoogle, DateTimeOffset CadastradoEm, IReadOnlyList<VendaExportada> Vendas);

/// <summary>
/// Os clientes do revendedor e os direitos do titular. O revendedor é o
/// controlador; a plataforma, a operadora. Exportar e anonimizar são do dono.
/// </summary>
public sealed class Clientes(IBancoDeDados banco, IRevendedorAtual atual, Auditoria.Auditoria auditoria, TimeProvider relogio)
{
    public async Task<IReadOnlyList<ClienteDaLista>> Listar(string? busca, CancellationToken ct)
    {
        var termo = (busca ?? string.Empty).Trim().ToLowerInvariant();
        var digitos = new string(termo.Where(char.IsAsciiDigit).ToArray());
        var consulta = banco.Clientes.AsNoTracking();
        if (termo.Length > 0)
        {
            // Vira lower(...) LIKE no Postgres: a cultura do .NET não entra na consulta.
#pragma warning disable CA1304, CA1311, CA1862
            consulta = consulta.Where(c => c.Nome.ToLower().Contains(termo) || c.Responsavel.ToLower().Contains(termo)
                || (digitos.Length >= 4 && (c.Telefone.Contains(digitos) || (c.Documento != null && c.Documento.Contains(digitos)))));
#pragma warning restore CA1304, CA1311, CA1862
        }

        return await consulta.OrderBy(c => c.Nome).Take(200)
            .Select(c => new ClienteDaLista(c.Id, c.Nome, c.Responsavel, c.Telefone, c.Documento, c.Email,
                banco.Vendas.Count(v => v.ClienteId == c.Id), c.CriadoEm, c.AnonimizadoEm != null))
            .ToListAsync(ct);
    }

    public async Task<DadosDoCliente> Exportar(Guid id, CancellationToken ct)
    {
        var cliente = await Achar(id, ct);
        var revendedor = await banco.Revendedores.AsNoTracking().Where(r => r.Id == atual.RevendedorId).Select(r => r.Nome).SingleAsync(ct);
        var vendas = await (
            from v in banco.Vendas.AsNoTracking().Where(v => v.ClienteId == id)
            join p in banco.Placas on v.PlacaId equals p.Id into placas
            from p in placas.DefaultIfEmpty()
            orderby v.VendidaEm
            select new { v.Id, v.VendidaEm, Codigo = p == null ? "" : p.Codigo, Destino = p == null ? null : p.Destino, VenceEm = p == null ? null : p.VenceEm, v.ValorCents })
            .ToListAsync(ct);
        var ids = vendas.Select(v => (Guid?)v.Id).ToList();
        var cobrancas = await banco.Cobrancas.AsNoTracking()
            .Where(c => c.Origem == OrigemDaCobranca.Venda && ids.Contains(c.OrigemId))
            .Select(c => new { c.OrigemId, c.CriadaEm, c.Meio, c.Situacao, c.ValorCents, c.PagaEm })
            .ToListAsync(ct);

        return new DadosDoCliente(relogio.GetUtcNow(), revendedor, cliente.Nome, cliente.Responsavel, cliente.Telefone, cliente.Documento,
            cliente.Email, cliente.LugarNoGoogle, cliente.CriadoEm,
            vendas.Select(v => new VendaExportada(v.VendidaEm, v.Codigo, v.Destino, v.VenceEm, v.ValorCents,
                cobrancas.Where(c => c.OrigemId == v.Id)
                    .Select(c => new CobrancaExportada(c.CriadaEm, c.Meio.ToString(), c.Situacao.ToString(), c.ValorCents, c.PagaEm)).ToList())).ToList());
    }

    /// <summary>
    /// Anonimiza o cliente. Placa no ar continua no ar (o destino é do negócio,
    /// não da pessoa), mas cobrança em aberto impede: a cobrança leva os dados do pagador.
    /// </summary>
    public async Task Anonimizar(Guid id, CancellationToken ct)
    {
        var cliente = await Achar(id, ct);
        var vendas = banco.Vendas.Where(v => v.ClienteId == id).Select(v => (Guid?)v.Id);
        if (await banco.Cobrancas.AnyAsync(c => c.Origem == OrigemDaCobranca.Venda && vendas.Contains(c.OrigemId) && c.Situacao == SituacaoDaCobranca.Pendente, ct))
        {
            throw new RegraDeNegocioException("Cliente com cobrança em aberto.");
        }

        cliente.Anonimizar(relogio.GetUtcNow());
        // Sem nome nem contato: a auditoria não pode guardar o que a anonimização apagou.
        auditoria.Registrar(AcaoAuditada.ClienteAnonimizado, "Cliente", cliente.Id.ToString());
        await banco.SaveChangesAsync(ct);
    }

    private async Task<Domain.Vendas.Cliente> Achar(Guid id, CancellationToken ct) =>
        await banco.Clientes.SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw new RegraDeNegocioException("Cliente não encontrado.");
}
