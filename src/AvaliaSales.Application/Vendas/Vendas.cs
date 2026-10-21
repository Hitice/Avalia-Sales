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
    MeioDePagamento Meio, SituacaoDaCobranca? Pagamento, DateTimeOffset VendidaEm, DateTimeOffset? PagaEm);

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

        var cliente = await banco.Clientes.SingleAsync(c => c.Id == venda.ClienteId, ct);
        var placa = await banco.Placas.AsNoTracking().SingleAsync(p => p.Id == venda.PlacaId, ct);
        var (cobranca, semCobranca) = await TentarCobrar(venda, cliente, placa.Codigo, null, 1, ct);
        return new VendaRegistrada(venda.Id, placa.Codigo, plataforma.Value.EnderecoDaPlaca(placa.Codigo), venda.ValorCents, placa.VenceEm, cobranca, semCobranca);
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
            select new { v, p.Codigo, Cliente = c.Nome, Produto = pr.Nome, Pagamento = cb == null ? (SituacaoDaCobranca?)null : cb.Situacao, PagaEm = cb == null ? null : cb.PagaEm })
            .Take(500)
            .ToListAsync(ct);
        var nomes = await contas.Nomes(linhas.Select(l => l.v.VendedorId).Distinct(), ct);
        return linhas.Select(l => new VendaDaLista(l.v.Id, l.Codigo, l.Cliente, l.Produto, l.v.VendedorId,
            nomes.GetValueOrDefault(l.v.VendedorId, "Não identificado"), l.v.ValorCents, l.v.ComissaoCents, l.v.Meio,
            l.Pagamento, l.v.VendidaEm, l.PagaEm)).ToList();
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
