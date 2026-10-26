using AvaliaSales.Application.Equipe;
using AvaliaSales.Application.Pagamentos;
using AvaliaSales.Application.Placas;
using AvaliaSales.Domain;
using AvaliaSales.Domain.Acesso;
using AvaliaSales.Domain.Auditoria;
using AvaliaSales.Domain.Financeiro;
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
    int Parcelas = 1,
    bool TesteGratuito = false,
    int Quantidade = 1);

/// <summary>Uma linha da venda feita junto com outras: placa pelo código, com o destino, ou serviço pela quantidade.</summary>
public sealed record ItemDaVenda(Guid ProdutoId, string? Codigo = null, DestinoInformado? Destino = null, long? ValorCents = null, int Quantidade = 1);

/// <summary>Várias linhas para o mesmo cliente, numa cobrança só.</summary>
public sealed record NovaVendaComItens(
    IReadOnlyList<ItemDaVenda> Itens,
    string Empresa,
    string Responsavel,
    string Telefone,
    string? Documento,
    string? Email,
    MeioDePagamento Meio = MeioDePagamento.Pix,
    string? TokenDoCartao = null,
    int Parcelas = 1,
    bool TesteGratuito = false);

public sealed record VendasRegistradas(IReadOnlyList<VendaRegistrada> Vendas, long TotalCents, CobrancaDto? Cobranca, string? SemCobranca);

/// <summary>A cobrança mais recente da venda, para a tela que espera o Pix.</summary>
public sealed record PagamentoDaVenda(SituacaoDaCobranca? Situacao, DateTimeOffset? PixExpiraEm);

/// <summary>Só o que muda: valor digitado errado, vendedor errado.</summary>
public sealed record CorrigirVenda(long? ValorCents = null, Guid? VendedorId = null);

/// <summary>O que acontece com a placa da venda desfeita.</summary>
public enum PlacaDaVendaCancelada
{
    /// <summary>Voltou inteira: vai ao estoque, para vender de novo.</summary>
    VoltaAoEstoque = 1,

    /// <summary>Voltou com defeito: sai de circulação.</summary>
    ComDefeito = 2,

    /// <summary>Não voltou: sai de circulação como perdida.</summary>
    NaoVoltou = 3,
}

/// <summary><paramref name="LinkCurto"/> - o /L/ do negócio, quando o destino é a avaliação no Google.</summary>
public sealed record VendaRegistrada(Guid VendaId, string Codigo, string Endereco, long ValorCents, DateTimeOffset? VenceEm, CobrancaDto? Cobranca, string? SemCobranca,
    string? LinkCurto = null);

/// <summary>Um dia (de Brasília) e quantas vendas valendo houve nele.</summary>
public sealed record VendasDoDia(DateOnly Dia, int Total);

/// <summary>
/// As vendas somadas inteiras, para a tela não somar só a página que abriu. <paramref name="NoMes"/> e
/// <paramref name="RecebidoNoMesCents"/> contam venda registrada do mês de Brasília; <paramref name="ComissaoEsperandoCents"/>
/// é a comissão das registradas ainda sem pagamento do cliente.
/// </summary>
public sealed record ResumoDasVendas(int Total, int Pagas, long ComissaoPagaCents, int NoMes, long RecebidoNoMesCents, long ComissaoEsperandoCents, IReadOnlyList<VendasDoDia> PorDia);

public sealed record VendaDaLista(
    Guid Id, string Codigo, string Cliente, string Produto, Guid VendedorId, string Vendedor, long ValorCents, long ComissaoCents,
    MeioDePagamento Meio, SituacaoDaCobranca? Pagamento, DateTimeOffset VendidaEm, DateTimeOffset? PagaEm, SituacaoDaVenda Situacao, bool Cortesia, MeioDePagamento? PagoPor,
    bool DoSistemaAnterior = false,
    MotivoDoCancelamento? MotivoDoCancelamento = null,
    DateTimeOffset? TesteAte = null,
    DateTimeOffset? TesteExpiradoEm = null,
    DateTimeOffset? TesteConvertidoEm = null,
    int Quantidade = 1,
    Guid? GrupoId = null,
    int ItensNoGrupo = 1);

/// <summary>
/// A venda no balcão: placa, produto, cliente, destino e cobrança. A venda é
/// gravada antes da cobrança: o Pagar.me fora do ar não segura o vendedor, e a
/// cobrança é gerada de novo pela lista de vendas.
/// </summary>
/// <summary>A venda aberta na tela dela: o que se vendeu, a quem, por quem, o pagamento, os itens que saíram juntos, as ordens de serviço e o razão.</summary>
public sealed record DetalheDaVenda(
    VendaDaLista Venda,
    ClienteDaVenda Cliente,
    CobrancaDaVenda? Cobranca,
    IReadOnlyList<ItemDoGrupo> Itens,
    IReadOnlyList<OrdemDaVenda> Ordens,
    IReadOnlyList<LinhaDaVenda> Razao,
    string? ObservacaoDoCancelamento,
    DateTimeOffset? CanceladaEm,
    DateTimeOffset? VenceEm,
    bool ComissaoRepassada);

public sealed record ClienteDaVenda(Guid Id, string Nome, string Responsavel, string Telefone, string? Documento, string? Email);

/// <summary><paramref name="PixCopiaECola"/> só com o Pix em aberto: o vendedor manda ao cliente que não está no balcão.</summary>
public sealed record CobrancaDaVenda(SituacaoDaCobranca Situacao, MeioDePagamento Meio, long ValorCents, long EstornadoCents, DateTimeOffset CriadaEm, DateTimeOffset? PagaEm, DateTimeOffset? PixExpiraEm,
    string? PixCopiaECola = null);

public sealed record ItemDoGrupo(Guid Id, string Produto, string Codigo, int Quantidade, long ValorCents, SituacaoDaVenda Situacao);

public sealed record OrdemDaVenda(long Numero, string Produto, int Quantidade, Domain.Servicos.SituacaoDaOrdem Situacao, DateTimeOffset CriadaEm, DateTimeOffset? EntregueEm);

public sealed record LinhaDaVenda(TipoDeLancamento Tipo, string Descricao, long ValorCents, string Competencia, DateTimeOffset OcorridoEm, bool Estorno);

public sealed class Vendas(
    IBancoDeDados banco,
    IUsuarioAtual usuario,
    IRevendedorAtual atual,
    Cobrancas cobrancas,
    IContas contas,
    Links.Links links,
    Auditoria.Auditoria auditoria,
    IOptions<OpcoesDaPlataforma> plataforma,
    Notificacoes.Notificacoes notificacoes,
    TimeProvider relogio,
    Financeiro.CustoDasPlacas custoDasPlacas,
    Clientes.PrivacidadeDoCliente privacidade,
    Financeiro.RecebimentosPorFora porFora)
{
    public const int ItensMaximos = 20;

    private static readonly TimeZoneInfo Brasilia = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    /// <summary>A venda de um item só, como a Nova venda fazia antes do carrinho.</summary>
    public async Task<VendaRegistrada> Registrar(NovaVenda pedido, CancellationToken ct)
    {
        var feitas = await Registrar(new NovaVendaComItens([new ItemDaVenda(pedido.ProdutoId, pedido.Codigo, pedido.Destino, pedido.ValorCents, pedido.Quantidade)],
            pedido.Empresa, pedido.Responsavel, pedido.Telefone, pedido.Documento, pedido.Email, pedido.Meio, pedido.TokenDoCartao, pedido.Parcelas, pedido.TesteGratuito), ct);
        return feitas.Vendas[0] with { Cobranca = feitas.Cobranca, SemCobranca = feitas.SemCobranca };
    }

    /// <summary>
    /// Uma ou mais linhas para o mesmo cliente: placa (pelo QR, com o destino)
    /// ou serviço que a Avalia executa (pela quantidade, nunca abaixo do pedido
    /// mínimo). Cada linha é uma venda, com a comissão dela; juntas formam um
    /// grupo e saem numa cobrança só. A ordem de serviço nasce quando o
    /// cliente paga (EfeitosDaCobranca).
    /// </summary>
    public async Task<VendasRegistradas> Registrar(NovaVendaComItens pedido, CancellationToken ct)
    {
        if (pedido.Meio is not (MeioDePagamento.Pix or MeioDePagamento.Cartao or MeioDePagamento.Link))
        {
            throw new RegraDeNegocioException("Escolha Pix ou cartão.");
        }

        if (pedido.Itens is not { Count: > 0 and <= ItensMaximos })
        {
            throw new RegraDeNegocioException($"A venda tem de 1 a {ItensMaximos} itens.");
        }

        var codigos = pedido.Itens.Select(i => CodigoDaPlaca.Normalizar(i.Codigo ?? "")).Where(c => c.Length > 0).ToList();
        if (codigos.Count != codigos.Distinct().Count())
        {
            throw new RegraDeNegocioException("A mesma placa está duas vezes na venda.");
        }

        (await banco.Revendedores.SingleAsync(r => r.Id == atual.RevendedorId, ct)).ExigirAtiva();
        var agora = relogio.GetUtcNow();

        // Produto, quantidade e destino conferidos antes de travar placa: o erro de digitação não segura o estoque.
        var linhas = new List<(ItemDaVenda Item, Domain.Catalogo.Produto Produto, Domain.Catalogo.ProdutoDaPlataforma? DoCatalogo, string? Destino)>();
        foreach (var item in pedido.Itens)
        {
            var (produto, doCatalogo) = await ProdutoDaVenda(item.ProdutoId, ct);
            if (doCatalogo is { UsaPlaca: false })
            {
                if (pedido.TesteGratuito)
                {
                    throw new RegraDeNegocioException("Teste gratuito é só para placa.");
                }

                if (item.Quantidade < doCatalogo.QuantidadeMinima)
                {
                    throw new RegraDeNegocioException($"{doCatalogo.Nome}: pedido mínimo de {doCatalogo.QuantidadeMinima} unidades.");
                }

                linhas.Add((item, produto, doCatalogo, null));
                continue;
            }

            if (item.Quantidade != 1)
            {
                throw new RegraDeNegocioException("Placa sai uma por código: leia cada QR.");
            }

            var destino = (item.Destino ?? throw new RegraDeNegocioException("Informe para onde a placa leva.")).Resolver(plataforma.Value.HostPublico);
            linhas.Add((item, produto, doCatalogo, destino));
        }

        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        var lugar = pedido.Itens.Select(i => i.Destino?.LugarNoGoogle).FirstOrDefault(l => l is not null);
        var cliente = await EncontrarCliente(pedido.Empresa, pedido.Responsavel, pedido.Telefone, pedido.Documento, pedido.Email, lugar, agora, ct);
        var feitas = new List<(Venda Venda, Placa? Placa, string? LinkCurto, string Descricao)>();
        foreach (var (item, produto, doCatalogo, destino) in linhas)
        {
            var (vendedorId, comissaoBps) = await QuemVende(produto.ComissaoBps, ct);
            if (destino is null)
            {
                var quantidade = item.Quantidade;
                var servico = new Venda(null, cliente.Id, produto.Id, checked(produto.PrecoCents * quantidade), vendedorId, item.ValorCents, 0, comissaoBps, pedido.Meio,
                    agora, checked(doCatalogo!.ParteDaAvaliaCents * quantidade), quantidade);
                banco.Vendas.Add(servico);
                feitas.Add((servico, null, null, quantidade > 1 ? $"{quantidade}x {produto.Nome}" : produto.Nome));
                continue;
            }

            var codigo = CodigoDaPlaca.Normalizar(item.Codigo ?? "");

            // Código de outro revendedor responde como inexistente: o código está impresso e qualquer um lê.
            var placa = (codigo.Length == 0 ? null : await banco.TravarPlaca(codigo, ct))
                ?? throw new RegraDeNegocioException("Código não encontrado, verifique novamente.");
            var venda = new Venda(placa.Id, cliente.Id, produto.Id, produto.PrecoCents, vendedorId, item.ValorCents,
                placa.CustoCents, comissaoBps, pedido.Meio, agora, doCatalogo?.ParteDaAvaliaCents ?? 0);
            placa.Vender(venda.Id, destino, agora);
            if (pedido.TesteGratuito)
            {
                venda.IniciarTeste(agora);
            }

            banco.Vendas.Add(venda);
            auditoria.Registrar(AcaoAuditada.DestinoDefinido, $"Placa {placa.Codigo}", placa.Codigo, null, destino);
            var linkCurto = item.Destino!.Tipo == TipoDeDestino.Google ? await links.DoNegocio(cliente.Id, cliente.Nome, destino, ct) : null;
            feitas.Add((venda, placa, linkCurto, $"Placa {placa.Codigo}"));
        }

        if (feitas.Count > 1)
        {
            foreach (var feita in feitas)
            {
                feita.Venda.EntrarNoGrupo(feitas[0].Venda.Id);
            }
        }

        banco.AvisarAoVivo("venda", atual.RevendedorId ?? Guid.Empty);
        await banco.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);

        // No teste a cobrança sai quando o cliente decidir, pela lista de vendas ou no balcão.
        var vendas = feitas.Select(f => f.Venda).ToList();
        var (cobranca, semCobranca) = vendas[0].TesteAte is { } ate
            ? (null, $"Teste gratuito até {ate.ToOffset(TimeSpan.FromHours(-3)):dd/MM}.")
            : await TentarCobrar(vendas, cliente, Descricao(feitas.Select(f => f.Descricao).ToList()), pedido.TokenDoCartao, pedido.Parcelas, ct);
        var registradas = feitas.Select(f => f.Placa is { } p
            ? new VendaRegistrada(f.Venda.Id, p.Codigo, plataforma.Value.EnderecoDaPlaca(p.Codigo), f.Venda.ValorCents, p.VenceEm, null, null, f.LinkCurto)
            : new VendaRegistrada(f.Venda.Id, "", "", f.Venda.ValorCents, null, null, null)).ToList();
        return new VendasRegistradas(registradas, vendas.Sum(v => v.ValorCents), cobranca, semCobranca);
    }

    /// <summary>O que aparece na fatura e no Pix: o item, ou o primeiro e quantos mais.</summary>
    private static string Descricao(List<string> itens) =>
        itens.Count == 1 ? itens[0] : $"{itens[0]} e mais {itens.Count - 1}";

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

    /// <summary>
    /// Gera de novo a cobrança que não saiu ou que falhou. A paga e a pendente
    /// ficam. No grupo, a cobrança nova é das vendas que seguem de pé.
    /// </summary>
    public async Task<VendaRegistrada> Cobrar(Guid vendaId, CancellationToken ct)
    {
        var venda = await AcharVenda(vendaId, ct);
        ExigirDaPlataforma(venda);
        await ExigirSemCobrancaViva(venda, "Esta venda já tem cobrança em aberto ou paga.", ct);
        if (venda.Situacao == SituacaoDaVenda.Cancelada || venda.Cortesia)
        {
            throw new RegraDeNegocioException("Venda cancelada ou cortesia não se cobra.");
        }

        var grupo = await DoGrupo(venda, ct);
        var cliente = await banco.Clientes.SingleAsync(c => c.Id == venda.ClienteId, ct);
        var descricoes = new List<string>();
        foreach (var linha in grupo)
        {
            descricoes.Add(linha.PlacaId is { } id
                ? "Placa " + await banco.Placas.Where(p => p.Id == id).Select(p => p.Codigo).SingleAsync(ct)
                : await banco.Produtos.Where(p => p.Id == linha.ProdutoId).Select(p => p.Nome).SingleAsync(ct));
        }

        var (cobranca, semCobranca) = await TentarCobrar(grupo, cliente, Descricao(descricoes), null, 1, ct);
        var placa = venda.PlacaId is { } placaId ? await banco.Placas.AsNoTracking().SingleAsync(p => p.Id == placaId, ct) : null;
        return placa is null
            ? new VendaRegistrada(venda.Id, "", "", venda.ValorCents, null, cobranca, semCobranca)
            : new VendaRegistrada(venda.Id, placa.Codigo, plataforma.Value.EnderecoDaPlaca(placa.Codigo), venda.ValorCents, placa.VenceEm, cobranca, semCobranca);
    }

    /// <summary>As vendas que a cobrança desta cobre: ela sozinha, ou as do grupo que seguem de pé e têm valor.</summary>
    private async Task<List<Venda>> DoGrupo(Venda venda, CancellationToken ct) =>
        venda.GrupoId is { } grupo
            ? await banco.Vendas.Where(v => v.GrupoId == grupo && v.Situacao == SituacaoDaVenda.Registrada && !v.Cortesia)
                .OrderBy(v => v.VendidaEm).ThenBy(v => v.Id).ToListAsync(ct)
            : [venda];

    /// <summary>
    /// O cliente pagou no balcão, em dinheiro ou por outro Pix. Só sem cobrança
    /// em aberto: o Pix em aberto ainda pode ser pago pelo QR, e o cliente
    /// pagaria duas vezes. A comissão passa a ser devida como no Pix.
    /// </summary>
    public async Task ReceberPorFora(Guid vendaId, MeioDePagamento meio, CancellationToken ct)
    {
        await using var trava = await TravarCobrancaDa(vendaId, ct);
        var venda = await AcharVenda(vendaId, ct);
        ExigirDaPlataforma(venda);
        if (venda.Situacao == SituacaoDaVenda.Cancelada || venda.Cortesia || venda.ValorCents == 0)
        {
            throw new RegraDeNegocioException("Esta venda não tem valor a receber.");
        }

        // O vendedor travado: dois cliques seguidos não passam juntos pelo limite do dia.
        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        await banco.TravarVendedor(venda.VendedorId, ct);
        await ExigirSemCobrancaViva(venda, "Venda com Pix em aberto ou já paga.", ct);
        var grupo = await DoGrupo(venda, ct);
        var total = grupo.Sum(v => v.ValorCents);
        await porFora.ExigirDentroDoLimite(venda.VendedorId, total, ct);
        var cobranca = await cobrancas.RegistrarPorFora(OrigemDaCobranca.Venda, venda.CobradaPor, meio, total, ct, grupo.Sum(v => v.ParteDaAvaliaCents));
        await RegistrarNaPlaca(venda, AcaoAuditada.RecebidoPorFora, $"{meio}, {Financeiro.RecebimentosPorFora.Reais(total)}", ct);
        var nomeDoCliente = await banco.Clientes.AsNoTracking().Where(c => c.Id == venda.ClienteId).Select(c => c.Nome).SingleAsync(ct);
        var item = venda.PlacaId is { } placaId ? "placa " + await banco.Placas.Where(p => p.Id == placaId).Select(p => p.Codigo).SingleAsync(ct) : "serviço";
        await porFora.Registrado(cobranca, venda.VendedorId, $"da venda de {nomeDoCliente} ({item})", $"/painel/vendas/{venda.Id}", venda.Id, ct);
        await banco.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
    }

    /// <summary>Confirmação por engano: o dono desfaz, e o razão recebe o estorno.</summary>
    public async Task DesfazerRecebimento(Guid vendaId, CancellationToken ct)
    {
        var venda = await AcharVenda(vendaId, ct);
        var cobranca = await banco.Cobrancas
            .Where(c => c.Origem == OrigemDaCobranca.Venda && c.OrigemId == venda.CobradaPor && c.Situacao == SituacaoDaCobranca.Paga)
            .SingleOrDefaultAsync(ct) ?? throw new RegraDeNegocioException("Esta venda não está paga.");
        if (venda.RepasseId is not null || (venda.GrupoId is { } grupo && await banco.Vendas.AnyAsync(v => v.GrupoId == grupo && v.RepasseId != null, ct)))
        {
            throw new RegraDeNegocioException("A comissão desta venda já foi repassada ao vendedor.");
        }

        await cobrancas.DesfazerPorFora(cobranca, ct);
        await RegistrarNaPlaca(venda, AcaoAuditada.RecebimentoDesfeito, null, ct);
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>A situação do pagamento, para placa e para serviço: a tela de resultado pergunta aqui até pagar ou o Pix vencer.</summary>
    public async Task<PagamentoDaVenda> Pagamento(Guid vendaId, CancellationToken ct)
    {
        var venda = await AcharVenda(vendaId, ct);
        var ultima = await banco.Cobrancas.AsNoTracking().Where(c => c.Origem == OrigemDaCobranca.Venda && c.OrigemId == venda.CobradaPor)
            .OrderByDescending(c => c.CriadaEm).Select(c => new { c.Situacao, c.PixExpiraEm }).FirstOrDefaultAsync(ct);
        return new PagamentoDaVenda(ultima?.Situacao, ultima?.PixExpiraEm);
    }

    /// <summary>
    /// Corrige a venda registrada errada, pelo dono, antes do repasse. O valor
    /// só muda sem cobrança viva nem paga: o que foi cobrado fica como foi. O
    /// vendedor muda também na venda paga: a comissão devida sai de um (estorno
    /// no razão) e entra no outro, pela regra de comissão dele.
    /// </summary>
    public async Task Corrigir(Guid vendaId, CorrigirVenda pedido, CancellationToken ct)
    {
        var venda = await AcharVenda(vendaId, ct);
        ExigirDaPlataforma(venda);
        if (venda.Situacao == SituacaoDaVenda.Cancelada)
        {
            throw new RegraDeNegocioException("Venda cancelada não se corrige.");
        }

        if (venda.RepasseId is not null)
        {
            throw new RegraDeNegocioException("A comissão desta venda já foi repassada ao vendedor.");
        }

        var agora = relogio.GetUtcNow();
        var antes = $"{venda.ValorCents} centavos, vendedor {venda.VendedorId}";
        if (pedido.ValorCents is { } valor && valor != venda.ValorCents)
        {
            await ExigirSemCobrancaViva(venda, "Venda com cobrança em aberto ou paga: o valor fica como foi cobrado.", ct);
            venda.CorrigirValor(valor);
        }

        if (pedido.VendedorId is { } novo && novo != venda.VendedorId)
        {
            var vendedor = await banco.Vendedores.AsNoTracking().SingleOrDefaultAsync(v => v.Id == novo, ct);
            int bps;
            if (vendedor is not null)
            {
                var doProduto = await banco.Produtos.Where(p => p.Id == venda.ProdutoId).Select(p => p.ComissaoBps).SingleAsync(ct);
                var padrao = await banco.Revendedores.Where(r => r.Id == venda.RevendedorId).Select(r => r.ComissaoPadraoBps).SingleAsync(ct);
                bps = vendedor.ComissaoBps ?? doProduto ?? padrao;
            }
            else if (novo == usuario.UsuarioId)
            {
                // O dono assume a venda: vende sem comissão, como no balcão.
                bps = 0;
            }
            else
            {
                throw new RegraDeNegocioException("Vendedor não encontrado.");
            }

            var paga = await banco.Cobrancas.Where(c => c.Origem == OrigemDaCobranca.Venda && c.OrigemId == venda.CobradaPor && c.Situacao == SituacaoDaCobranca.Paga)
                .Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);

            // A comissão de cada vendedor nesta cobrança é uma linha só no razão: a que voltasse ao primeiro repetiria a dele.
            if (paga is { } jaPaga && await banco.Lancamentos.AnyAsync(l => l.OrigemId == jaPaga && l.VendaId == venda.Id && l.Tipo == TipoDeLancamento.ComissaoDevida
                    && l.EstornaId == null && l.VendedorId == novo, ct))
            {
                throw new RegraDeNegocioException("A comissão desta venda paga já foi deste vendedor. Para devolver a ele, cancele a venda e registre de novo.");
            }

            var estornadas = banco.Lancamentos.Where(l => l.EstornaId != null).Select(l => l.EstornaId);
            var devidas = await banco.Lancamentos.Where(l => l.VendaId == venda.Id && l.Tipo == TipoDeLancamento.ComissaoDevida && l.EstornaId == null
                && !estornadas.Contains(l.Id)).ToListAsync(ct);
            foreach (var linha in devidas)
            {
                banco.Lancamentos.Add(linha.Estorno("venda passada a outro vendedor", agora));
            }

            venda.TrocarVendedor(novo, bps);

            // Venda paga: a comissão do vendedor certo passa a ser devida, mesmo quando a de antes era zero (o dono vendia).
            if (paga is { } cobrancaId && venda.ComissaoCents > 0)
            {
                var item = venda.PlacaId is { } placaId ? "placa " + await banco.Placas.Where(p => p.Id == placaId).Select(p => p.Codigo).SingleAsync(ct) : "venda de serviço";
                banco.Lancamentos.Add(new Lancamento(TipoDeLancamento.ComissaoDevida, cobrancaId, -venda.ComissaoCents, $"Comissão da {item}", agora, venda.Id, novo));
            }
        }

        await RegistrarNaPlaca(venda, AcaoAuditada.VendaCorrigida, $"{venda.ValorCents} centavos, vendedor {venda.VendedorId}", ct, antes);
        banco.AvisarAoVivo("venda", venda.RevendedorId);
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Desfaz a venda com o motivo. A cobrança viva é cancelada antes: o Pix em
    /// aberto deixa de poder ser pago, o pago volta ao cliente pelo Pagar.me e o
    /// recebido por fora é desfeito, com o estorno no razão (inclusive da
    /// comissão; se já foi repassada, desconta no próximo repasse). A placa sai
    /// do ar e volta ao estoque, ou sai de circulação se veio com defeito ou não
    /// voltou.
    /// </summary>
    public async Task Cancelar(Guid vendaId, MotivoDoCancelamento motivo, string? observacao, PlacaDaVendaCancelada placaVolta, CancellationToken ct)
    {
        // Dois cancelamentos da mesma venda ao mesmo tempo (duplo clique, duas abas) não devolvem duas vezes ao cliente: o segundo espera e já a vê cancelada.
        await using var trava = await TravarCobrancaDa(vendaId, ct);
        var venda = await AcharVenda(vendaId, ct);
        ExigirDaPlataforma(venda);
        if (venda.Situacao == SituacaoDaVenda.Cancelada)
        {
            throw new RegraDeNegocioException("Esta venda já foi cancelada.");
        }

        if (venda.PlacaId is { } daVenda)
        {
            await ExigirSemRenovacaoAberta(daVenda, ct);
        }

        // A venda se cancela antes da cobrança: o motivo inválido recusa antes de o dinheiro voltar, e o estorno já a vê cancelada.
        var agora = relogio.GetUtcNow();
        venda.Cancelar(motivo, observacao, agora);
        var vivas = await banco.Cobrancas.Where(c => c.Origem == OrigemDaCobranca.Venda && c.OrigemId == venda.CobradaPor
            && (c.Situacao == SituacaoDaCobranca.Pendente || c.Situacao == SituacaoDaCobranca.Paga)).ToListAsync(ct);
        var estornou = vivas.Any(c => c.Situacao == SituacaoDaCobranca.Paga);
        foreach (var cobranca in vivas)
        {
            // No grupo pago, só esta volta ao cliente; o Pix em aberto do grupo cai inteiro, e o resto ganha cobrança nova em Vendas.
            if (venda.GrupoId is not null && cobranca.Situacao == SituacaoDaCobranca.Paga)
            {
                await cobrancas.EstornarVenda(cobranca, venda.Id, ct);
            }
            else
            {
                await cobrancas.Cancelar(cobranca, ct);
            }
        }

        await notificacoes.EncerrarDaVenda(venda.Id, ct);
        if (venda.VendedorId != usuario.UsuarioId && await banco.Vendedores.AnyAsync(v => v.Id == venda.VendedorId, ct))
        {
            var cliente = await banco.Clientes.AsNoTracking().Where(c => c.Id == venda.ClienteId).Select(c => c.Nome).SingleAsync(ct);
            var comissao = estornou && venda.ComissaoCents > 0 ? " A comissão foi estornada." : string.Empty;
            notificacoes.ParaVendedor(venda.RevendedorId, venda.VendedorId, "Venda cancelada", $"{cliente}: {Motivo(motivo)}.{comissao}", "/vendedor/inicio");
        }

        if (venda.PlacaId is { } placaId)
        {
            var placa = await banco.Placas.SingleAsync(p => p.Id == placaId, ct);
            auditoria.Registrar(AcaoAuditada.VendaCancelada, $"Venda da placa {placa.Codigo} ({motivo})", placa.Codigo, placa.Destino, null);
            placa.DesfazerVenda();
            if (placaVolta != PlacaDaVendaCancelada.VoltaAoEstoque)
            {
                placa.Baixar(placaVolta == PlacaDaVendaCancelada.NaoVoltou ? MotivoDaBaixa.Perdida : MotivoDaBaixa.Quebrada, agora);
                custoDasPlacas.Baixada(placa, agora);
            }

            await custoDasPlacas.VendaCancelada(venda, agora, ct);
        }
        else
        {
            auditoria.Registrar(AcaoAuditada.VendaCancelada, "Venda de serviço", venda.Id.ToString(), null, null);
        }

        banco.AvisarAoVivo("venda", venda.RevendedorId);
        await banco.SaveChangesAsync(ct);
    }

    private static string Motivo(MotivoDoCancelamento motivo) => motivo switch
    {
        MotivoDoCancelamento.ErroDeCadastro => "cancelada por erro de cadastro",
        MotivoDoCancelamento.Inadimplencia => "cancelada por falta de pagamento",
        MotivoDoCancelamento.Desistencia => "o cliente desistiu",
        MotivoDoCancelamento.Defeito => "cancelada por defeito",
        _ => "venda cancelada",
    };

    /// <summary>A revenda dá a placa: valor e comissão zerados, com a mesma trava da cobrança.</summary>
    public async Task Cortesia(Guid vendaId, CancellationToken ct)
    {
        var venda = await AcharVenda(vendaId, ct);
        ExigirDaPlataforma(venda);
        await ExigirSemCobrancaViva(venda, "Venda com cobrança em aberto ou paga.", ct);
        var valorAntes = venda.ValorCents;
        venda.TornarCortesia();
        custoDasPlacas.Cortesia(venda, relogio.GetUtcNow());
        await RegistrarNaPlaca(venda, AcaoAuditada.VendaEmCortesia, $"{valorAntes} centavos", ct);
        banco.AvisarAoVivo("venda", venda.RevendedorId);
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>
    /// A venda passa para outra placa do estoque, com o mesmo destino e o mesmo
    /// vencimento, e a antiga é encerrada para sempre com o motivo (defeito de
    /// impressão, quebrada, perdida). O dono troca com qualquer placa livre; o
    /// vendedor, na venda dele e com placa que está com ele.
    /// </summary>
    public async Task<string> TrocarPlaca(Guid vendaId, string codigoNovo, MotivoDaBaixa? motivo, CancellationToken ct)
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

        if (usuario.Papel == Papel.Vendedor && nova.ConsignadaA != usuario.UsuarioId)
        {
            throw new RegraDeNegocioException("Troque por uma placa que está com você.");
        }

        nova.ReceberVendaDe(antiga, motivo, relogio.GetUtcNow());
        custoDasPlacas.Baixada(antiga, relogio.GetUtcNow());
        venda.TrocarPlaca(nova.Id);
        // Nas duas placas: a ficha de cada uma conta para onde a venda foi ou de onde veio.
        auditoria.Registrar(AcaoAuditada.PlacaTrocada, $"Placa {antiga.Codigo} ({antiga.MotivoDaBaixa})", antiga.Codigo, antiga.Codigo, nova.Codigo);
        auditoria.Registrar(AcaoAuditada.PlacaTrocada, $"Placa {nova.Codigo}", nova.Codigo, antiga.Codigo, nova.Codigo);
        banco.AvisarAoVivo("venda", venda.RevendedorId);
        await banco.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
        return nova.Codigo;
    }

    private async Task RegistrarNaPlaca(Venda venda, AcaoAuditada acao, string? depois, CancellationToken ct, string? antes = null)
    {
        if (venda.PlacaId is not { } placaId)
        {
            auditoria.Registrar(acao, "Venda de serviço", venda.Id.ToString(), antes, depois);
            return;
        }

        var codigo = await banco.Placas.Where(p => p.Id == placaId).Select(p => p.Codigo).SingleAsync(ct);
        auditoria.Registrar(acao, $"Venda da placa {codigo}", codigo, antes, depois);
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
        if (await banco.Cobrancas.AnyAsync(c => c.Origem == OrigemDaCobranca.Venda && c.OrigemId == venda.CobradaPor
                && (c.Situacao == SituacaoDaCobranca.Pendente || c.Situacao == SituacaoDaCobranca.Paga), ct))
        {
            throw new RegraDeNegocioException(recusa);
        }
    }

    /// <summary>
    /// Uma venda com tudo o que ela tocou. O vendedor só abre a dele e não vê o
    /// razão; o filtro do revendedor faz o resto.
    /// </summary>
    public async Task<DetalheDaVenda> Detalhe(Guid id, CancellationToken ct)
    {
        var vendedor = usuario.Papel == Papel.Vendedor;
        var venda = await banco.Vendas.AsNoTracking().SingleOrDefaultAsync(v => v.Id == id && (!vendedor || v.VendedorId == usuario.UsuarioId), ct)
            ?? throw new RegraDeNegocioException("Venda não encontrada.");
        var linha = (await Listar(id, ct)).Single();

        var inteiro = await banco.Clientes.AsNoTracking().Where(c => c.Id == venda.ClienteId)
            .Select(c => new ClienteDaVenda(c.Id, c.Nome, c.Responsavel, c.Telefone, c.Documento, c.Email)).SingleAsync(ct);
        var contato = Clientes.PrivacidadeDoCliente.Ver(await privacidade.Libera(inteiro.Id, ct), inteiro.Telefone, inteiro.Documento, inteiro.Email);
        var cliente = inteiro with { Telefone = contato.Telefone, Documento = contato.Documento, Email = contato.Email };
        var cobranca = await banco.Cobrancas.AsNoTracking()
            .Where(c => c.Origem == OrigemDaCobranca.Venda && c.OrigemId == venda.CobradaPor).OrderByDescending(c => c.CriadaEm)
            .Select(c => new CobrancaDaVenda(c.Situacao, c.Meio, c.ValorCents, c.EstornadoCents, c.CriadaEm, c.PagaEm, c.PixExpiraEm,
                c.Situacao == SituacaoDaCobranca.Pendente ? c.PixCopiaECola : null)).FirstOrDefaultAsync(ct);
        var itens = venda.GrupoId is { } grupo
            ? await (from v in banco.Vendas.AsNoTracking()
                     where v.GrupoId == grupo && v.Id != id
                     join pr in banco.Produtos on v.ProdutoId equals pr.Id
                     join p in banco.Placas on v.PlacaId equals p.Id into placas
                     from p in placas.DefaultIfEmpty()
                     orderby v.VendidaEm
                     select new ItemDoGrupo(v.Id, pr.Nome, p == null ? "" : p.Codigo, v.Quantidade, v.ValorCents, v.Situacao)).ToListAsync(ct)
            : [];
        var ordens = await banco.OrdensDeServico.AsNoTracking().Where(o => o.VendaId == id).OrderBy(o => o.CriadaEm)
            .Select(o => new OrdemDaVenda(o.Numero, o.Produto, o.Quantidade, o.Situacao, o.CriadaEm, o.EntregueEm)).ToListAsync(ct);
        var razao = vendedor ? [] : await banco.Lancamentos.AsNoTracking().Where(l => l.VendaId == id).OrderBy(l => l.OcorridoEm)
            .Select(l => new LinhaDaVenda(l.Tipo, l.Descricao, l.ValorCents, l.Competencia, l.OcorridoEm, l.EstornaId != null)).ToListAsync(ct);
        var venceEm = venda.PlacaId is { } placa
            ? await banco.Placas.AsNoTracking().Where(p => p.Id == placa).Select(p => p.VenceEm).SingleOrDefaultAsync(ct)
            : null;

        return new DetalheDaVenda(linha, cliente, cobranca, itens, ordens, razao, venda.ObservacaoDoCancelamento, venda.CanceladaEm, venceEm, venda.RepasseId is not null);
    }

    /// <summary>
    /// A página das vendas, da mais nova para a mais antiga (o Id desempata). O vendedor só vê as dele.
    /// Com <paramref name="busca"/>, o texto no cliente, no produto, na placa ou no nome do vendedor.
    /// </summary>
    public async Task<Pagina<VendaDaLista>> Listar(string? busca, PedidoDePagina pedido, CancellationToken ct)
    {
        var (pagina, tamanho) = Paginacao.Normalizar(pedido);
        var (itens, total) = await Ler(null, busca, Paginacao.Pular(pagina, tamanho), tamanho, ct);
        return Paginacao.Montar(itens, total, pagina, tamanho);
    }

    /// <summary>
    /// Os números que as telas mostram sobre as vendas inteiras, e não sobre a página aberta: totais, o mês de
    /// Brasília e os últimos 30 dias. O vendedor soma só as dele.
    /// </summary>
    public async Task<ResumoDasVendas> Resumo(CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        var local = TimeZoneInfo.ConvertTime(agora, Brasilia);
        var inicioDoMes = new DateTimeOffset(local.Year, local.Month, 1, 0, 0, 0, local.Offset).ToUniversalTime();
        var hoje = DateOnly.FromDateTime(local.DateTime);
        var primeiroDia = hoje.AddDays(-29);
        var inicioDaJanela = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(primeiroDia.ToDateTime(TimeOnly.MinValue), Brasilia), TimeSpan.Zero);

        var consulta = banco.Vendas.AsNoTracking();
        if (usuario.Papel == Papel.Vendedor)
        {
            consulta = consulta.Where(v => v.VendedorId == usuario.UsuarioId);
        }

        var comPagamento = consulta.Select(v => new
        {
            v.VendidaEm,
            v.ValorCents,
            // A mesma conta de Venda.ComissaoCents, que o SQL não enxerga por ser propriedade calculada.
            ComissaoCents = ((v.ValorCents - v.ParteDaAvaliaCents) * v.ComissaoBps + Domain.Equipe.Comissao.Base / 2) / Domain.Equipe.Comissao.Base,
            v.Situacao,
            v.Cortesia,
            Pagamento = banco.Cobrancas.Where(k => k.Origem == OrigemDaCobranca.Venda && k.OrigemId == (v.GrupoId ?? v.Id))
                .OrderByDescending(k => k.CriadaEm).ThenByDescending(k => k.Id).Select(k => (SituacaoDaCobranca?)k.Situacao).FirstOrDefault(),
        });
        var soma = await comPagamento.GroupBy(_ => 1).Select(g => new
        {
            Total = g.Count(),
            Pagas = g.Count(x => x.Pagamento == SituacaoDaCobranca.Paga),
            ComissaoPaga = g.Where(x => x.Pagamento == SituacaoDaCobranca.Paga).Sum(x => x.ComissaoCents),
            NoMes = g.Count(x => x.Situacao == SituacaoDaVenda.Registrada && x.VendidaEm >= inicioDoMes),
            Recebido = g.Where(x => x.Situacao == SituacaoDaVenda.Registrada && x.VendidaEm >= inicioDoMes && x.Pagamento == SituacaoDaCobranca.Paga).Sum(x => x.ValorCents),
            Esperando = g.Where(x => x.Situacao == SituacaoDaVenda.Registrada && x.Pagamento != SituacaoDaCobranca.Paga && !x.Cortesia && x.ComissaoCents > 0).Sum(x => x.ComissaoCents),
        }).SingleOrDefaultAsync(ct);

        var instantes = await consulta.Where(v => v.Situacao == SituacaoDaVenda.Registrada && v.VendidaEm >= inicioDaJanela).Select(v => v.VendidaEm).ToListAsync(ct);
        var porDia = instantes.GroupBy(i => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(i, Brasilia).DateTime))
            .Where(g => g.Key >= primeiroDia && g.Key <= hoje)
            .OrderBy(g => g.Key).Select(g => new VendasDoDia(g.Key, g.Count())).ToList();
        return new ResumoDasVendas(soma?.Total ?? 0, soma?.Pagas ?? 0, soma?.ComissaoPaga ?? 0, soma?.NoMes ?? 0, soma?.Recebido ?? 0, soma?.Esperando ?? 0, porDia);
    }

    private async Task<IReadOnlyList<VendaDaLista>> Listar(Guid so, CancellationToken ct) => (await Ler(so, null, 0, 1, ct)).Itens;

    private async Task<(IReadOnlyList<VendaDaLista> Itens, int Total)> Ler(Guid? so, string? busca, int pular, int tamanho, CancellationToken ct)
    {
        var consulta = banco.Vendas.AsNoTracking();
        if (so is { } apenas)
        {
            consulta = consulta.Where(v => v.Id == apenas);
        }

        if (usuario.Papel == Papel.Vendedor)
        {
            consulta = consulta.Where(v => v.VendedorId == usuario.UsuarioId);
        }

        var termo = Paginacao.Termo(busca);
        var juntas =
            from v in consulta
            join p in banco.Placas on v.PlacaId equals p.Id into placas
            from p in placas.DefaultIfEmpty()
            join c in banco.Clientes on v.ClienteId equals c.Id
            join pr in banco.Produtos on v.ProdutoId equals pr.Id
            select new { v, p, Cliente = c.Nome, Produto = pr.Nome };
        if (termo.Length > 0)
        {
            juntas = juntas.Where(x => Busca.Normalizar(x.Cliente).Contains(Busca.Normalizar(termo)) || Busca.Normalizar(x.Produto).Contains(Busca.Normalizar(termo))
                || (x.p != null && Busca.Normalizar(x.p.Codigo).Contains(Busca.Normalizar(termo)))
                || banco.Vendedores.Any(e => e.Id == x.v.VendedorId && Busca.Normalizar(e.Nome).Contains(Busca.Normalizar(termo))));
        }

        var total = await juntas.CountAsync(ct);
        var linhas = total <= pular ? [] : await juntas
            .OrderByDescending(x => x.v.VendidaEm).ThenByDescending(x => x.v.Id)
            .Skip(pular).Take(tamanho)
            .Select(x => new
            {
                x.v,
                Codigo = x.p == null ? "" : x.p.Codigo,
                x.Cliente,
                x.Produto,
                Cobranca = banco.Cobrancas.Where(k => k.Origem == OrigemDaCobranca.Venda && k.OrigemId == (x.v.GrupoId ?? x.v.Id)).OrderByDescending(k => k.CriadaEm).ThenByDescending(k => k.Id)
                    .Select(k => new { k.Situacao, k.PagaEm, k.Meio }).FirstOrDefault(),
            })
            .ToListAsync(ct);
        var nomes = await contas.Nomes(linhas.Select(l => l.v.VendedorId).Distinct(), ct);
        // Os itens do grupo vêm inteiros, e não só os que caíram nesta página.
        var grupos = linhas.Where(l => l.v.GrupoId != null).Select(l => l.v.GrupoId).Distinct().ToList();
        var itensDoGrupo = grupos.Count == 0 ? [] : await banco.Vendas.AsNoTracking().Where(v => grupos.Contains(v.GrupoId))
            .GroupBy(v => v.GrupoId).Select(g => new { Grupo = g.Key, Itens = g.Count() }).ToDictionaryAsync(x => x.Grupo!.Value, x => x.Itens, ct);
        return (linhas.Select(l => new VendaDaLista(l.v.Id, l.Codigo, l.Cliente, l.Produto, l.v.VendedorId,
            nomes.GetValueOrDefault(l.v.VendedorId, "Não identificado"), l.v.ValorCents, l.v.ComissaoCents, l.v.Meio,
            l.Cobranca?.Situacao, l.v.VendidaEm, l.Cobranca?.PagaEm, l.v.Situacao, l.v.Cortesia, l.Cobranca?.Meio, l.v.DoSistemaAnterior,
            l.v.MotivoDoCancelamento, l.v.TesteAte, l.v.TesteExpiradoEm, l.v.TesteConvertidoEm, l.v.Quantidade, l.v.GrupoId,
            l.v.GrupoId is { } g ? itensDoGrupo.GetValueOrDefault(g, 1) : 1)).ToList(), total);
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
    private async Task<Cliente> EncontrarCliente(string empresa, string responsavel, string telefone, string? documento, string? email, string? lugarNoGoogle,
        DateTimeOffset agora, CancellationToken ct)
    {
        var novo = new Cliente(empresa, responsavel, telefone, documento, agora);
        novo.Completar(null, email, lugarNoGoogle);

        var existente = novo.Documento is { } doc
            ? await banco.Clientes.SingleOrDefaultAsync(c => c.Documento == doc, ct)
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
        List<Venda> vendas, Cliente cliente, string descricao, string? tokenDoCartao, int parcelas, CancellationToken ct)
    {
        if (cliente.Documento is null || cliente.Email is null)
        {
            return (null, "Para gerar a cobrança, informe o CNPJ ou CPF e o e-mail do cliente.");
        }

        try
        {
            var cobranca = await cobrancas.Criar(new CriarCobranca(OrigemDaCobranca.Venda, vendas[0].CobradaPor, vendas[0].Meio, vendas.Sum(v => v.ValorCents),
                descricao, new Pagador(cliente.Nome, cliente.Email, cliente.Documento, cliente.Telefone), tokenDoCartao, parcelas, vendas.Sum(v => v.ParteDaAvaliaCents)), ct);
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

    /// <summary>A trava da cobrança que cobre a venda (a dela, ou a do grupo), antes de ler o estado que ela protege.</summary>
    private async Task<IAsyncDisposable> TravarCobrancaDa(Guid vendaId, CancellationToken ct)
    {
        var origem = await banco.Vendas.AsNoTracking().Where(v => v.Id == vendaId).Select(v => v.GrupoId ?? v.Id).SingleOrDefaultAsync(ct);
        return await banco.SegurarOrigemDaCobranca(origem == Guid.Empty ? vendaId : origem, ct);
    }

    private async Task<Venda> AcharVenda(Guid id, CancellationToken ct)
    {
        var venda = await banco.Vendas.SingleOrDefaultAsync(v => v.Id == id, ct) ?? throw new RegraDeNegocioException("Venda não encontrada.");
        return usuario.Papel == Papel.Vendedor && venda.VendedorId != usuario.UsuarioId
            ? throw new RegraDeNegocioException("Venda não encontrada.")
            : venda;
    }
}
