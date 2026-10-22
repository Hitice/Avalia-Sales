using AvaliaSales.Application.Equipe;
using AvaliaSales.Application.Pagamentos;
using AvaliaSales.Application.Placas;
using AvaliaSales.Domain;
using AvaliaSales.Domain.Acesso;
using AvaliaSales.Domain.Pagamentos;
using AvaliaSales.Domain.Placas;
using AvaliaSales.Domain.Vendas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Application.Vendas;

public sealed record NovaVenda(
    string Codigo,
    Guid ProdutoId,
    string Empresa,
    string Responsavel,
    string Telefone,
    string? Documento,
    string? Email,
    DestinoInformado Destino,
    long? ValorCents,
    MeioDePagamento Meio = MeioDePagamento.Pix,
    string? TokenDoCartao = null,
    int Parcelas = 1);

public sealed record VendaRegistrada(Guid VendaId, string Codigo, string Endereco, long ValorCents, DateTimeOffset? VenceEm, CobrancaDto? Cobranca, string? SemCobranca);

public sealed record VendaDaLista(
    Guid Id, string Codigo, string Cliente, string Produto, Guid VendedorId, string Vendedor, long ValorCents, long ComissaoCents,
    MeioDePagamento Meio, SituacaoDaCobranca? Pagamento, DateTimeOffset VendidaEm, DateTimeOffset? PagaEm, SituacaoDaVenda Situacao, bool Cortesia, MeioDePagamento? PagoPor);

/// <summary>
/// A venda no balcão: placa, produto, cliente, destino e cobrança. A venda é
/// gravada antes da cobrança: o Pagar.me fora do ar não segura o vendedor, e a
/// cobrança é gerada de novo pela lista de vendas.
/// </summary>
public sealed class Vendas(
    IBancoDeDados banco,
    IUsuarioAtual usuario,
    IRevendedorAtual atual,
    Cobrancas cobrancas,
    IContas contas,
    IOptions<OpcoesDaPlataforma> plataforma,
    TimeProvider relogio)
{
    public async Task<VendaRegistrada> Registrar(NovaVenda pedido, CancellationToken ct)
    {
        if (pedido.Meio is not (MeioDePagamento.Pix or MeioDePagamento.Cartao or MeioDePagamento.Link))
        {
            throw new RegraDeNegocioException("A venda nasce com Pix ou cartão. Recebido em dinheiro se confirma depois, na lista de vendas.");
        }

        var (vendedorId, comissaoBps) = await QuemVende(ct);
        var produto = await banco.Produtos.AsNoTracking().SingleOrDefaultAsync(p => p.Id == pedido.ProdutoId && p.Ativo, ct)
            ?? throw new RegraDeNegocioException("Este produto não está no catálogo.");
        var codigo = CodigoDaPlaca.Normalizar(pedido.Codigo);
        var destino = pedido.Destino.Resolver(plataforma.Value.HostPublico);
        var agora = relogio.GetUtcNow();

        await using var transacao = await banco.Database.BeginTransactionAsync(ct);

        // Código de outro revendedor responde como inexistente: o código está impresso e qualquer um lê.
        var placa = codigo.Length == 0
            ? null
            : await banco.TravarPlaca(codigo, ct);
        if (placa is null)
        {
            throw new RegraDeNegocioException("Código não encontrado, verifique novamente.");
        }

        var cliente = await EncontrarCliente(pedido, agora, ct);
        var venda = new Venda(placa.Id, cliente.Id, produto.Id, produto.PrecoCents, vendedorId, pedido.ValorCents,
            placa.CustoCents, comissaoBps, pedido.Meio, agora);
        placa.Vender(venda.Id, destino, agora);
        banco.Vendas.Add(venda);
        banco.AvisarAoVivo("venda", placa.RevendedorId);
        await banco.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);

        var (cobranca, semCobranca) = await TentarCobrar(venda, cliente, placa.Codigo, pedido.TokenDoCartao, pedido.Parcelas, ct);
        return new VendaRegistrada(venda.Id, placa.Codigo, plataforma.Value.EnderecoDaPlaca(placa.Codigo), venda.ValorCents, placa.VenceEm, cobranca, semCobranca);
    }

    /// <summary>Gera de novo a cobrança que não saiu ou que falhou. A paga e a pendente ficam.</summary>
    public async Task<VendaRegistrada> Cobrar(Guid vendaId, CancellationToken ct)
    {
        var venda = await AcharVenda(vendaId, ct);
        if (await banco.Cobrancas.AnyAsync(c => c.Origem == OrigemDaCobranca.Venda && c.OrigemId == venda.Id
                && (c.Situacao == SituacaoDaCobranca.Pendente || c.Situacao == SituacaoDaCobranca.Paga), ct))
        {
            throw new RegraDeNegocioException("Esta venda já tem cobrança em aberto ou paga.");
        }

        if (venda.Situacao == SituacaoDaVenda.Cancelada || venda.Cortesia)
        {
            throw new RegraDeNegocioException("Venda cancelada ou cortesia não se cobra.");
        }

        var cliente = await banco.Clientes.SingleAsync(c => c.Id == venda.ClienteId, ct);
        var placa = await banco.Placas.AsNoTracking().SingleAsync(p => p.Id == venda.PlacaId, ct);
        var (cobranca, semCobranca) = await TentarCobrar(venda, cliente, placa.Codigo, null, 1, ct);
        return new VendaRegistrada(venda.Id, placa.Codigo, plataforma.Value.EnderecoDaPlaca(placa.Codigo), venda.ValorCents, placa.VenceEm, cobranca, semCobranca);
    }

    /// <summary>
    /// O cliente pagou no balcão, em dinheiro ou por outro Pix. Só sem cobrança
    /// em aberto: o Pix em aberto ainda pode ser pago pelo QR, e o cliente
    /// pagaria duas vezes. A comissão passa a ser devida como no Pix.
    /// </summary>
    public async Task ReceberPorFora(Guid vendaId, MeioDePagamento meio, CancellationToken ct)
    {
        var venda = await AcharVenda(vendaId, ct);
        if (venda.Situacao == SituacaoDaVenda.Cancelada || venda.Cortesia || venda.ValorCents == 0)
        {
            throw new RegraDeNegocioException("Esta venda não tem valor a receber.");
        }

        await ExigirSemCobrancaViva(venda, "Esta venda tem Pix em aberto ou já está paga. Espere o Pix vencer para confirmar por fora.", ct);
        await cobrancas.RegistrarPorFora(OrigemDaCobranca.Venda, venda.Id, meio, venda.ValorCents, ct);
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>Confirmação por engano: o dono desfaz, e o razão recebe o estorno.</summary>
    public async Task DesfazerRecebimento(Guid vendaId, CancellationToken ct)
    {
        var venda = await AcharVenda(vendaId, ct);
        var cobranca = await banco.Cobrancas
            .Where(c => c.Origem == OrigemDaCobranca.Venda && c.OrigemId == venda.Id && c.Situacao == SituacaoDaCobranca.Paga)
            .SingleOrDefaultAsync(ct) ?? throw new RegraDeNegocioException("Esta venda não está paga.");
        if (venda.RepasseId is not null)
        {
            throw new RegraDeNegocioException("A comissão desta venda já foi repassada ao vendedor.");
        }

        await cobrancas.DesfazerPorFora(cobranca, ct);
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Desfaz a venda. Só sem cobrança em aberto nem paga: Pix em aberto ainda
    /// pode cair, e o pagamento chegaria a uma venda desfeita. A placa volta ao
    /// estoque e sai do ar.
    /// </summary>
    public async Task Cancelar(Guid vendaId, CancellationToken ct)
    {
        var venda = await AcharVenda(vendaId, ct);
        await ExigirSemCobrancaViva(venda, "Esta venda tem cobrança em aberto ou paga. Espere o Pix vencer, ou estorne no Pagar.me, antes de cancelar.", ct);
        await ExigirSemRenovacaoAberta(venda.PlacaId, ct);
        var placa = await banco.Placas.SingleAsync(p => p.Id == venda.PlacaId, ct);
        venda.Cancelar();
        placa.DesfazerVenda();
        banco.AvisarAoVivo("venda", venda.RevendedorId);
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>A revenda dá a placa: valor e comissão zerados, com a mesma trava da cobrança.</summary>
    public async Task Cortesia(Guid vendaId, CancellationToken ct)
    {
        var venda = await AcharVenda(vendaId, ct);
        await ExigirSemCobrancaViva(venda, "Esta venda tem cobrança em aberto ou paga; cortesia é para venda sem cobrança.", ct);
        venda.TornarCortesia();
        banco.AvisarAoVivo("venda", venda.RevendedorId);
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>A venda passa para outra placa do estoque, e a antiga é encerrada para sempre.</summary>
    public async Task<string> TrocarPlaca(Guid vendaId, string codigoNovo, CancellationToken ct)
    {
        var venda = await AcharVenda(vendaId, ct);
        var codigo = CodigoDaPlaca.Normalizar(codigoNovo);
        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        var nova = (codigo.Length == 0 ? null : await banco.TravarPlaca(codigo, ct))
            ?? throw new RegraDeNegocioException("Código não encontrado, verifique novamente.");
        await ExigirSemRenovacaoAberta(venda.PlacaId, ct);
        var antiga = await banco.Placas.SingleAsync(p => p.Id == venda.PlacaId, ct);
        if (nova.Id == antiga.Id)
        {
            throw new RegraDeNegocioException("A placa nova é a mesma da venda.");
        }

        nova.ReceberVendaDe(antiga);
        venda.TrocarPlaca(nova.Id);
        banco.AvisarAoVivo("venda", venda.RevendedorId);
        await banco.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
        return nova.Codigo;
    }

    /// <summary>A renovação em aberto aponta para a placa: paga depois, estenderia uma placa fora da venda.</summary>
    private async Task ExigirSemRenovacaoAberta(Guid placaId, CancellationToken ct)
    {
        if (await banco.Renovacoes.AnyAsync(r => r.PlacaId == placaId && r.Situacao == SituacaoDaRenovacao.Aberta, ct))
        {
            throw new RegraDeNegocioException("A placa tem renovação em aberto. Espere o Pix dela vencer.");
        }
    }

    private async Task ExigirSemCobrancaViva(Venda venda, string recusa, CancellationToken ct)
    {
        if (await banco.Cobrancas.AnyAsync(c => c.Origem == OrigemDaCobranca.Venda && c.OrigemId == venda.Id
                && (c.Situacao == SituacaoDaCobranca.Pendente || c.Situacao == SituacaoDaCobranca.Paga), ct))
        {
            throw new RegraDeNegocioException(recusa);
        }
    }

    public async Task<IReadOnlyList<VendaDaLista>> Listar(CancellationToken ct)
    {
        var consulta = banco.Vendas.AsNoTracking();
        if (usuario.Papel == Papel.Vendedor)
        {
            consulta = consulta.Where(v => v.VendedorId == usuario.UsuarioId);
        }

        var linhas = await (
            from v in consulta
            join p in banco.Placas on v.PlacaId equals p.Id
            join c in banco.Clientes on v.ClienteId equals c.Id
            join pr in banco.Produtos on v.ProdutoId equals pr.Id
            let cb = banco.Cobrancas.Where(x => x.Origem == OrigemDaCobranca.Venda && x.OrigemId == v.Id).OrderByDescending(x => x.CriadaEm).FirstOrDefault()
            orderby v.VendidaEm descending
            select new { v, p.Codigo, Cliente = c.Nome, Produto = pr.Nome, Pagamento = cb == null ? (SituacaoDaCobranca?)null : cb.Situacao, PagaEm = cb == null ? null : cb.PagaEm, PagoPor = cb == null ? (MeioDePagamento?)null : cb.Meio })
            .Take(500)
            .ToListAsync(ct);
        var nomes = await contas.Nomes(linhas.Select(l => l.v.VendedorId).Distinct(), ct);
        return linhas.Select(l => new VendaDaLista(l.v.Id, l.Codigo, l.Cliente, l.Produto, l.v.VendedorId,
            nomes.GetValueOrDefault(l.v.VendedorId, "Não identificado"), l.v.ValorCents, l.v.ComissaoCents, l.v.Meio,
            l.Pagamento, l.v.VendidaEm, l.PagaEm, l.v.Situacao, l.v.Cortesia, l.PagoPor)).ToList();
    }

    /// <summary>
    /// Dono vende sem comissão: a parte dele é o que sobra. Vendedor vende com a
    /// comissão dele, ou com a padrão do revendedor.
    /// </summary>
    private async Task<(Guid VendedorId, int ComissaoBps)> QuemVende(CancellationToken ct)
    {
        var id = usuario.UsuarioId ?? throw new InvalidOperationException("Venda sem usuário.");
        if (usuario.Papel == Papel.Dono)
        {
            return (id, 0);
        }

        var vendedor = await banco.Vendedores.AsNoTracking().SingleOrDefaultAsync(v => v.Id == id && v.Ativo, ct)
            ?? throw new RegraDeNegocioException("Seu cadastro de vendedor está desativado.");
        var padrao = await banco.Revendedores.Where(r => r.Id == atual.RevendedorId).Select(r => r.ComissaoPadraoBps).SingleAsync(ct);
        return (id, vendedor.ComissaoBps ?? padrao);
    }

    /// <summary>Um cliente por documento ou telefone: o que já existe é achado e só ganha o que faltava.</summary>
    private async Task<Cliente> EncontrarCliente(NovaVenda pedido, DateTimeOffset agora, CancellationToken ct)
    {
        var novo = new Cliente(pedido.Empresa, pedido.Responsavel, pedido.Telefone, pedido.Documento, agora);
        novo.Completar(null, pedido.Email, pedido.Destino.LugarNoGoogle);

        var existente = novo.Documento is { } documento
            ? await banco.Clientes.SingleOrDefaultAsync(c => c.Documento == documento, ct)
            : null;
        existente ??= await banco.Clientes.FirstOrDefaultAsync(c => c.Telefone == novo.Telefone, ct);
        if (existente is null)
        {
            banco.Clientes.Add(novo);
            return novo;
        }

        existente.Completar(novo.Documento, novo.Email, novo.LugarNoGoogle);
        return existente;
    }

    private async Task<(CobrancaDto? Cobranca, string? SemCobranca)> TentarCobrar(
        Venda venda, Cliente cliente, string codigo, string? tokenDoCartao, int parcelas, CancellationToken ct)
    {
        if (cliente.Documento is null || cliente.Email is null)
        {
            return (null, "Para gerar a cobrança, informe o CNPJ ou CPF e o e-mail do cliente.");
        }

        try
        {
            var cobranca = await cobrancas.Criar(new CriarCobranca(OrigemDaCobranca.Venda, venda.Id, venda.Meio, venda.ValorCents,
                $"Placa {codigo}", new Pagador(cliente.Nome, cliente.Email, cliente.Documento, cliente.Telefone), tokenDoCartao, parcelas), ct);
            return (cobranca, null);
        }
        catch (RegraDeNegocioException recusa)
        {
            return (null, recusa.Message);
        }
        catch (IntegracaoException)
        {
            return (null, "O Pagar.me não respondeu. Gere a cobrança de novo na lista de vendas.");
        }
    }

    private async Task<Venda> AcharVenda(Guid id, CancellationToken ct)
    {
        var venda = await banco.Vendas.SingleOrDefaultAsync(v => v.Id == id, ct) ?? throw new RegraDeNegocioException("Venda não encontrada.");
        return usuario.Papel == Papel.Vendedor && venda.VendedorId != usuario.UsuarioId
            ? throw new RegraDeNegocioException("Venda não encontrada.")
            : venda;
    }
}
