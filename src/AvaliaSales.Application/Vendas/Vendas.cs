using AvaliaSales.Application.Equipe;
using AvaliaSales.Application.Pagamentos;
using AvaliaSales.Application.Placas;
using AvaliaSales.Domain;
using AvaliaSales.Domain.Acesso;
using AvaliaSales.Domain.Auditoria;
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
    DestinoInformado? Destino,
    long? ValorCents,
    MeioDePagamento Meio = MeioDePagamento.Pix,
    string? TokenDoCartao = null,
    int Parcelas = 1);

/// <summary><paramref name="LinkCurto"/> - o /L/ do negócio, quando o destino é a avaliação no Google.</summary>
public sealed record VendaRegistrada(Guid VendaId, string Codigo, string Endereco, long ValorCents, DateTimeOffset? VenceEm, CobrancaDto? Cobranca, string? SemCobranca,
    string? LinkCurto = null);

public sealed record VendaDaLista(
    Guid Id, string Codigo, string Cliente, string Produto, Guid VendedorId, string Vendedor, long ValorCents, long ComissaoCents,
    MeioDePagamento Meio, SituacaoDaCobranca? Pagamento, DateTimeOffset VendidaEm, DateTimeOffset? PagaEm, SituacaoDaVenda Situacao, bool Cortesia, MeioDePagamento? PagoPor,
    bool DoSistemaAnterior = false);

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
    Links.Links links,
    Auditoria.Auditoria auditoria,
    IOptions<OpcoesDaPlataforma> plataforma,
    TimeProvider relogio)
{
    public async Task<VendaRegistrada> Registrar(NovaVenda pedido, CancellationToken ct)
    {
        if (pedido.Meio is not (MeioDePagamento.Pix or MeioDePagamento.Cartao or MeioDePagamento.Link))
        {
            throw new RegraDeNegocioException("Escolha Pix ou cartão.");
        }

        var (produto, doCatalogo) = await ProdutoDaVenda(pedido.ProdutoId, ct);
        var (vendedorId, comissaoBps) = await QuemVende(produto.ComissaoBps, ct);
        var agora = relogio.GetUtcNow();
        if (doCatalogo is { UsaPlaca: false })
        {
            return await RegistrarServico(pedido, produto, doCatalogo.ParteDaAvaliaCents, vendedorId, comissaoBps, agora, ct);
        }

        var codigo = CodigoDaPlaca.Normalizar(pedido.Codigo);
        var destino = (pedido.Destino ?? throw new RegraDeNegocioException("Informe para onde a placa leva.")).Resolver(plataforma.Value.HostPublico);

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
            placa.CustoCents, comissaoBps, pedido.Meio, agora, doCatalogo?.ParteDaAvaliaCents ?? 0);
        placa.Vender(venda.Id, destino, agora);
        banco.Vendas.Add(venda);
        auditoria.Registrar(AcaoAuditada.DestinoDefinido, $"Placa {placa.Codigo}", placa.Codigo, null, destino);
        var linkCurto = pedido.Destino.Tipo == TipoDeDestino.Google ? await links.DoNegocio(cliente.Id, cliente.Nome, destino, ct) : null;
        banco.AvisarAoVivo("venda", placa.RevendedorId);
        await banco.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);

        var (cobranca, semCobranca) = await TentarCobrar(venda, cliente, $"Placa {placa.Codigo}", pedido.TokenDoCartao, pedido.Parcelas, ct);
        return new VendaRegistrada(venda.Id, placa.Codigo, plataforma.Value.EnderecoDaPlaca(placa.Codigo), venda.ValorCents, placa.VenceEm, cobranca, semCobranca, linkCurto);
    }

    /// <summary>
    /// Serviço que a Avalia executa: sem placa nem destino. A ordem de serviço
    /// nasce quando o cliente paga (EfeitosDaCobranca).
    /// </summary>
    private async Task<VendaRegistrada> RegistrarServico(NovaVenda pedido, Domain.Catalogo.Produto produto, long parteDaAvaliaCents, Guid vendedorId, int comissaoBps,
        DateTimeOffset agora, CancellationToken ct)
    {
        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        var cliente = await EncontrarCliente(pedido, agora, ct);
        var venda = new Venda(null, cliente.Id, produto.Id, produto.PrecoCents, vendedorId, pedido.ValorCents, 0, comissaoBps, pedido.Meio, agora, parteDaAvaliaCents);
        banco.Vendas.Add(venda);
        banco.AvisarAoVivo("venda", atual.RevendedorId ?? Guid.Empty);
        await banco.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);

        var (cobranca, semCobranca) = await TentarCobrar(venda, cliente, produto.Nome, pedido.TokenDoCartao, pedido.Parcelas, ct);
        return new VendaRegistrada(venda.Id, "", "", venda.ValorCents, null, cobranca, semCobranca);
    }

    /// <summary>
    /// O produto vem pela tabela da revenda (o id de lá) ou pelo catálogo da
    /// Avalia (o id do produto da plataforma). Na primeira venda pelo catálogo,
    /// a tabela da revenda ganha a linha com o preço sugerido.
    /// </summary>
    private async Task<(Domain.Catalogo.Produto Produto, Domain.Catalogo.ProdutoDaPlataforma? DoCatalogo)> ProdutoDaVenda(Guid id, CancellationToken ct)
    {
        var daRevenda = await banco.Produtos.SingleOrDefaultAsync(p => p.Id == id && p.Ativo, ct);
        var catalogoId = daRevenda?.ProdutoDaPlataformaId ?? (daRevenda is null ? id : null);
        var doCatalogo = catalogoId is { } c ? await banco.ProdutosDaPlataforma.AsNoTracking().SingleOrDefaultAsync(p => p.Id == c, ct) : null;
        if (daRevenda is null && doCatalogo is null)
        {
            throw new RegraDeNegocioException("Este produto não está no catálogo.");
        }

        if (doCatalogo is { AVenda: false })
        {
            throw new RegraDeNegocioException($"{doCatalogo.Nome} não está à venda agora.");
        }

        if (daRevenda is null)
        {
            daRevenda = await banco.Produtos.SingleOrDefaultAsync(p => p.ProdutoDaPlataformaId == doCatalogo!.Id, ct);
            if (daRevenda is null)
            {
                daRevenda = Domain.Catalogo.Produto.DaPlataforma(doCatalogo!);
                banco.Produtos.Add(daRevenda);
            }
            else if (!daRevenda.Ativo)
            {
                throw new RegraDeNegocioException("Este produto está desligado na sua tabela.");
            }
        }

        return (daRevenda, doCatalogo);
    }

    /// <summary>Gera de novo a cobrança que não saiu ou que falhou. A paga e a pendente ficam.</summary>
    public async Task<VendaRegistrada> Cobrar(Guid vendaId, CancellationToken ct)
    {
        var venda = await AcharVenda(vendaId, ct);
        ExigirDaPlataforma(venda);
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
        var placa = venda.PlacaId is { } placaId ? await banco.Placas.AsNoTracking().SingleAsync(p => p.Id == placaId, ct) : null;
        if (placa is null)
        {
            var nome = await banco.Produtos.Where(p => p.Id == venda.ProdutoId).Select(p => p.Nome).SingleAsync(ct);
            var (doServico, semCobrancaDoServico) = await TentarCobrar(venda, cliente, nome, null, 1, ct);
            return new VendaRegistrada(venda.Id, "", "", venda.ValorCents, null, doServico, semCobrancaDoServico);
        }

        var (cobranca, semCobranca) = await TentarCobrar(venda, cliente, $"Placa {placa.Codigo}", null, 1, ct);
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
        ExigirDaPlataforma(venda);
        if (venda.Situacao == SituacaoDaVenda.Cancelada || venda.Cortesia || venda.ValorCents == 0)
        {
            throw new RegraDeNegocioException("Esta venda não tem valor a receber.");
        }

        await ExigirSemCobrancaViva(venda, "Venda com Pix em aberto ou já paga.", ct);
        await cobrancas.RegistrarPorFora(OrigemDaCobranca.Venda, venda.Id, meio, venda.ValorCents, ct, venda.ParteDaAvaliaCents);
        await RegistrarNaPlaca(venda, AcaoAuditada.RecebidoPorFora, meio.ToString(), ct);
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
        await RegistrarNaPlaca(venda, AcaoAuditada.RecebimentoDesfeito, null, ct);
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
        ExigirDaPlataforma(venda);
        await ExigirSemCobrancaViva(venda, "Venda com cobrança em aberto ou paga.", ct);
        venda.Cancelar();
        if (venda.PlacaId is { } placaId)
        {
            await ExigirSemRenovacaoAberta(placaId, ct);
            var placa = await banco.Placas.SingleAsync(p => p.Id == placaId, ct);
            auditoria.Registrar(AcaoAuditada.VendaCancelada, $"Venda da placa {placa.Codigo}", placa.Codigo, placa.Destino, null);
            placa.DesfazerVenda();
        }
        else
        {
            auditoria.Registrar(AcaoAuditada.VendaCancelada, "Venda de serviço", venda.Id.ToString(), null, null);
        }

        banco.AvisarAoVivo("venda", venda.RevendedorId);
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>A revenda dá a placa: valor e comissão zerados, com a mesma trava da cobrança.</summary>
    public async Task Cortesia(Guid vendaId, CancellationToken ct)
    {
        var venda = await AcharVenda(vendaId, ct);
        ExigirDaPlataforma(venda);
        await ExigirSemCobrancaViva(venda, "Venda com cobrança em aberto ou paga.", ct);
        var valorAntes = venda.ValorCents;
        venda.TornarCortesia();
        await RegistrarNaPlaca(venda, AcaoAuditada.VendaEmCortesia, $"{valorAntes} centavos", ct);
        banco.AvisarAoVivo("venda", venda.RevendedorId);
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>A venda passa para outra placa do estoque, e a antiga é encerrada para sempre.</summary>
    public async Task<string> TrocarPlaca(Guid vendaId, string codigoNovo, CancellationToken ct)
    {
        var venda = await AcharVenda(vendaId, ct);
        var codigo = CodigoDaPlaca.Normalizar(codigoNovo);
        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        var placaDaVenda = venda.PlacaId ?? throw new RegraDeNegocioException("Venda de serviço não tem placa.");
        var nova = (codigo.Length == 0 ? null : await banco.TravarPlaca(codigo, ct))
            ?? throw new RegraDeNegocioException("Código não encontrado, verifique novamente.");
        await ExigirSemRenovacaoAberta(placaDaVenda, ct);
        var antiga = await banco.Placas.SingleAsync(p => p.Id == placaDaVenda, ct);
        if (nova.Id == antiga.Id)
        {
            throw new RegraDeNegocioException("A placa nova é a mesma da venda.");
        }

        nova.ReceberVendaDe(antiga);
        venda.TrocarPlaca(nova.Id);
        // Nas duas placas: a ficha de cada uma conta para onde a venda foi ou de onde veio.
        auditoria.Registrar(AcaoAuditada.PlacaTrocada, $"Placa {antiga.Codigo}", antiga.Codigo, antiga.Codigo, nova.Codigo);
        auditoria.Registrar(AcaoAuditada.PlacaTrocada, $"Placa {nova.Codigo}", nova.Codigo, antiga.Codigo, nova.Codigo);
        banco.AvisarAoVivo("venda", venda.RevendedorId);
        await banco.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
        return nova.Codigo;
    }

    private async Task RegistrarNaPlaca(Venda venda, AcaoAuditada acao, string? depois, CancellationToken ct)
    {
        if (venda.PlacaId is not { } placaId)
        {
            auditoria.Registrar(acao, "Venda de serviço", venda.Id.ToString(), null, depois);
            return;
        }

        var codigo = await banco.Placas.Where(p => p.Id == placaId).Select(p => p.Codigo).SingleAsync(ct);
        auditoria.Registrar(acao, $"Venda da placa {codigo}", codigo, null, depois);
    }

    /// <summary>
    /// A renovação em aberto aponta para a placa: paga depois, estenderia uma
    /// placa fora da venda. Só trava com Pix vivo; sem ele, ninguém mais paga, e
    /// a renovação esquecida é cancelada aqui, em vez de travar a venda para sempre.
    /// </summary>
    private async Task ExigirSemRenovacaoAberta(Guid placaId, CancellationToken ct)
    {
        var renovacao = await banco.Renovacoes.SingleOrDefaultAsync(r => r.PlacaId == placaId && r.Situacao == SituacaoDaRenovacao.Aberta, ct);
        if (renovacao is null)
        {
            return;
        }

        if (await banco.Cobrancas.AnyAsync(c => c.Origem == OrigemDaCobranca.Renovacao && c.OrigemId == renovacao.Id
                && (c.Situacao == SituacaoDaCobranca.Pendente || c.Situacao == SituacaoDaCobranca.Paga), ct))
        {
            throw new RegraDeNegocioException("Placa com renovação em aberto.");
        }

        renovacao.Cancelar();
    }

    /// <summary>A troca de placa vale para qualquer venda; o resto só para a feita aqui.</summary>
    private static void ExigirDaPlataforma(Venda venda)
    {
        if (venda.DoSistemaAnterior)
        {
            throw new RegraDeNegocioException("Venda paga no sistema anterior.");
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
            join p in banco.Placas on v.PlacaId equals p.Id into placas
            from p in placas.DefaultIfEmpty()
            join c in banco.Clientes on v.ClienteId equals c.Id
            join pr in banco.Produtos on v.ProdutoId equals pr.Id
            let cb = banco.Cobrancas.Where(x => x.Origem == OrigemDaCobranca.Venda && x.OrigemId == v.Id).OrderByDescending(x => x.CriadaEm).FirstOrDefault()
            orderby v.VendidaEm descending
            select new { v, Codigo = p == null ? "" : p.Codigo, Cliente = c.Nome, Produto = pr.Nome, Pagamento = cb == null ? (SituacaoDaCobranca?)null : cb.Situacao, PagaEm = cb == null ? null : cb.PagaEm, PagoPor = cb == null ? (MeioDePagamento?)null : cb.Meio })
            .Take(500)
            .ToListAsync(ct);
        var nomes = await contas.Nomes(linhas.Select(l => l.v.VendedorId).Distinct(), ct);
        return linhas.Select(l => new VendaDaLista(l.v.Id, l.Codigo, l.Cliente, l.Produto, l.v.VendedorId,
            nomes.GetValueOrDefault(l.v.VendedorId, "Não identificado"), l.v.ValorCents, l.v.ComissaoCents, l.v.Meio,
            l.Pagamento, l.v.VendidaEm, l.PagaEm, l.v.Situacao, l.v.Cortesia, l.PagoPor, l.v.DoSistemaAnterior)).ToList();
    }

    /// <summary>
    /// Dono vende sem comissão: a parte dele é o que sobra. Vendedor vende com a
    /// mais específica - a dele, a do produto, a padrão do revendedor.
    /// </summary>
    private async Task<(Guid VendedorId, int ComissaoBps)> QuemVende(int? doProduto, CancellationToken ct)
    {
        var id = usuario.UsuarioId ?? throw new InvalidOperationException("Venda sem usuário.");
        if (usuario.Papel == Papel.Dono)
        {
            return (id, 0);
        }

        var vendedor = await banco.Vendedores.AsNoTracking().SingleOrDefaultAsync(v => v.Id == id && v.Ativo, ct)
            ?? throw new RegraDeNegocioException("Seu cadastro de vendedor está desativado.");
        var padrao = await banco.Revendedores.Where(r => r.Id == atual.RevendedorId).Select(r => r.ComissaoPadraoBps).SingleAsync(ct);
        return (id, vendedor.ComissaoBps ?? doProduto ?? padrao);
    }

    /// <summary>Um cliente por documento ou telefone: o que já existe é achado e só ganha o que faltava.</summary>
    private async Task<Cliente> EncontrarCliente(NovaVenda pedido, DateTimeOffset agora, CancellationToken ct)
    {
        var novo = new Cliente(pedido.Empresa, pedido.Responsavel, pedido.Telefone, pedido.Documento, agora);
        novo.Completar(null, pedido.Email, pedido.Destino?.LugarNoGoogle);

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
        Venda venda, Cliente cliente, string descricao, string? tokenDoCartao, int parcelas, CancellationToken ct)
    {
        if (cliente.Documento is null || cliente.Email is null)
        {
            return (null, "Para gerar a cobrança, informe o CNPJ ou CPF e o e-mail do cliente.");
        }

        try
        {
            var cobranca = await cobrancas.Criar(new CriarCobranca(OrigemDaCobranca.Venda, venda.Id, venda.Meio, venda.ValorCents,
                descricao, new Pagador(cliente.Nome, cliente.Email, cliente.Documento, cliente.Telefone), tokenDoCartao, parcelas, venda.ParteDaAvaliaCents), ct);
            return (cobranca, null);
        }
        catch (RegraDeNegocioException recusa)
        {
            return (null, recusa.Message);
        }
        catch (IntegracaoException)
        {
            return (null, "O Pagar.me não respondeu. Gere a cobrança de novo em Vendas.");
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
