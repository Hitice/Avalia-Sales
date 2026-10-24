using AvaliaSales.Domain.Auditoria;
using AvaliaSales.Domain.Pagamentos;
using AvaliaSales.Domain;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Clientes;

public sealed record ClienteDaLista(Guid Id, string Nome, string Responsavel, string Telefone, string? Documento, string? Email, int Placas, DateTimeOffset CriadoEm, bool Anonimizado);

public sealed record AlterarCliente(string Nome, string Responsavel, string? Telefone, string? Documento, string? Email, string? LugarNoGoogle);

/// <summary><paramref name="Estado"/> - o EstadoDaPlaca de hoje, por nome; nulo na venda de serviço.</summary>
public sealed record VendaDoCliente(Guid VendaId, string? Codigo, string Produto, Domain.Vendas.SituacaoDaVenda Situacao, string? Estado,
    DateTimeOffset VendidaEm, DateTimeOffset? VenceEm, long ValorCents);

public sealed record FichaDoCliente(Guid Id, string Nome, string Responsavel, string Telefone, string? Documento, string? Email, string? LugarNoGoogle,
    DateTimeOffset CriadoEm, bool Anonimizado, IReadOnlyList<VendaDoCliente> Vendas);

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
                banco.Vendas.Count(v => v.ClienteId == c.Id && v.PlacaId != null && v.Situacao == Domain.Vendas.SituacaoDaVenda.Registrada), c.CriadoEm, c.AnonimizadoEm != null))
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

    /// <summary>O cliente com as vendas dele, de placa e de serviço, e o estado de cada placa hoje.</summary>
    public async Task<FichaDoCliente> Ficha(Guid id, CancellationToken ct)
    {
        var c = await Achar(id, ct);
        var agora = relogio.GetUtcNow();
        var vendas = await (
            from v in banco.Vendas.AsNoTracking().Where(v => v.ClienteId == id)
            join p in banco.Placas on v.PlacaId equals p.Id into placas
            from p in placas.DefaultIfEmpty()
            join pr in banco.Produtos on v.ProdutoId equals pr.Id
            orderby v.VendidaEm descending
            select new { v.Id, Codigo = p == null ? null : p.Codigo, Produto = pr.Nome, v.Situacao, Placa = p == null ? (Domain.Placas.SituacaoDaPlaca?)null : p.Situacao,
                VenceEm = p == null ? null : p.VenceEm, v.VendidaEm, v.ValorCents })
            .ToListAsync(ct);
        return new FichaDoCliente(c.Id, c.Nome, c.Responsavel, c.Telefone, c.Documento, c.Email, c.LugarNoGoogle, c.CriadoEm, c.AnonimizadoEm is not null,
            vendas.Select(v => new VendaDoCliente(v.Id, v.Codigo, v.Produto, v.Situacao,
                v.Placa is { } s ? Domain.Placas.Placa.EstadoDe(s, v.VenceEm, agora).ToString() : null, v.VendidaEm, v.VenceEm, v.ValorCents)).ToList());
    }

    /// <summary>Documento repetido no revendedor é o mesmo cliente: em vez de recusar no banco, a tela sugere juntar.</summary>
    /// <summary>
    /// Cliente cadastrado à mão, antes da venda. O mesmo CPF, CNPJ ou telefone
    /// já cadastrado é recusado: a venda acharia os dois e juntaria depois.
    /// </summary>
    public async Task<Guid> Cadastrar(AlterarCliente pedido, CancellationToken ct)
    {
        var cliente = new Domain.Vendas.Cliente(pedido.Nome, pedido.Responsavel, pedido.Telefone ?? string.Empty, pedido.Documento, relogio.GetUtcNow());
        cliente.Completar(null, pedido.Email, pedido.LugarNoGoogle);
        if (cliente.Documento is { } documento && await banco.Clientes.AnyAsync(c => c.Documento == documento, ct))
        {
            throw new RegraDeNegocioException("Já existe cliente com este CPF ou CNPJ. Abra a ficha dele.");
        }

        if (await banco.Clientes.AnyAsync(c => c.Telefone == cliente.Telefone, ct))
        {
            throw new RegraDeNegocioException("Já existe cliente com este telefone. Abra a ficha dele.");
        }

        banco.Clientes.Add(cliente);
        auditoria.Registrar(AcaoAuditada.ClienteCadastrado, $"Cliente {cliente.Nome}", cliente.Id.ToString(), null,
            $"{cliente.Nome}, {cliente.Telefone}, {cliente.Documento}, {cliente.Email}");
        await banco.SaveChangesAsync(ct);
        return cliente.Id;
    }

    public async Task Alterar(Guid id, AlterarCliente pedido, CancellationToken ct)
    {
        var cliente = await Achar(id, ct);
        var antes = $"{cliente.Nome}, {cliente.Telefone}, {cliente.Documento}, {cliente.Email}";
        cliente.Alterar(pedido.Nome, pedido.Responsavel, pedido.Telefone, pedido.Documento, pedido.Email, pedido.LugarNoGoogle);
        if (cliente.Documento is { } documento && await banco.Clientes.AnyAsync(c => c.Id != id && c.Documento == documento, ct))
        {
            throw new RegraDeNegocioException("Já existe outro cliente com este CPF ou CNPJ. Junte os dois.");
        }

        auditoria.Registrar(AcaoAuditada.ClienteAlterado, $"Cliente {cliente.Nome}", cliente.Id.ToString(), antes,
            $"{cliente.Nome}, {cliente.Telefone}, {cliente.Documento}, {cliente.Email}");
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>Só cliente sem venda: o que tem venda fica, por obrigação fiscal, e o caminho é anonimizar.</summary>
    public async Task Excluir(Guid id, CancellationToken ct)
    {
        var cliente = await Achar(id, ct);
        if (await banco.Vendas.AnyAsync(v => v.ClienteId == id, ct))
        {
            throw new RegraDeNegocioException("Cliente com vendas não se exclui. Anonimize, se ele pedir.");
        }

        banco.Clientes.Remove(cliente);
        auditoria.Registrar(AcaoAuditada.ClienteExcluido, "Cliente", cliente.Id.ToString());
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Dois cadastros do mesmo negócio viram um: vendas, links e placas
    /// transferidas passam ao que fica, que ganha o que lhe faltava
    /// (documento, e-mail, lugar no Google), e o outro deixa de existir.
    /// </summary>
    public async Task Juntar(Guid ficaId, Guid saiId, CancellationToken ct)
    {
        if (ficaId == saiId)
        {
            throw new RegraDeNegocioException("Escolha dois clientes diferentes.");
        }

        var fica = await Achar(ficaId, ct);
        var sai = await Achar(saiId, ct);
        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        foreach (var venda in await banco.Vendas.Where(v => v.ClienteId == saiId).ToListAsync(ct))
        {
            venda.PassarAoCliente(ficaId);
        }

        foreach (var link in await banco.Links.Where(l => l.ClienteId == saiId).ToListAsync(ct))
        {
            link.PassarAoCliente(ficaId);
        }

        foreach (var placa in await banco.Placas.Where(p => p.ClienteDaTransferenciaId == saiId).ToListAsync(ct))
        {
            placa.PassarAoCliente(ficaId);
        }

        var (documento, email, lugar) = (sai.Documento, sai.Email, sai.LugarNoGoogle);
        banco.Clientes.Remove(sai);
        auditoria.Registrar(AcaoAuditada.ClientesJuntados, $"Cliente {fica.Nome}", fica.Id.ToString(), $"{sai.Nome} ({sai.Id})", fica.Nome);
        await banco.SaveChangesAsync(ct);

        // Depois de o outro sair: o documento dele não pode existir em dois cadastros ao mesmo tempo.
        fica.Completar(documento, email, lugar);
        await banco.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
    }

    private async Task<Domain.Vendas.Cliente> Achar(Guid id, CancellationToken ct) =>
        await banco.Clientes.SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw new RegraDeNegocioException("Cliente não encontrado.");
}
