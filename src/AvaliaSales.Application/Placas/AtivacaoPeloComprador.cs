using AvaliaSales.Application.Equipe;
using AvaliaSales.Domain;
using AvaliaSales.Domain.Auditoria;
using AvaliaSales.Domain.Catalogo;
using AvaliaSales.Domain.Placas;
using AvaliaSales.Domain.Vendas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Application.Placas;

/// <summary>Avaliação leva cada placa ao Google do estabelecimento; cardápio leva todas as placas do lote a um link só.</summary>
public enum TipoDeAtivacao
{
    Avaliacao = 1,
    Cardapio = 2,
}

public sealed record PlacaParaAtivar(string Codigo, bool Ativa, string? Destino, string Endereco);

/// <summary>O que o comprador vê depois de digitar o código de ativação do pedido.</summary>
public sealed record LoteParaAtivar(string Rotulo, TipoDeAtivacao Tipo, IReadOnlyList<PlacaParaAtivar> Placas)
{
    public bool PlacaUnica => Placas.Count == 1;

    public bool TodasAtivas => Placas.Count > 0 && Placas.All(p => p.Ativa);
}

/// <summary>
/// <paramref name="CodigoDaPlaca"/> é o código impresso na placa, obrigatório no lote de avaliação com mais de uma placa;
/// no cardápio o link vale para todas e o código é ignorado.
/// </summary>
public sealed record AtivacaoDoComprador(string? CodigoDaPlaca, string Empresa, string Telefone, DestinoInformado Destino);

public sealed record TrocaDeDestinoDoComprador(string? CodigoDaPlaca, DestinoInformado Destino);

/// <summary>As placas que passaram a levar ao destino, com o endereço que vai na tag NFC (o mesmo do QR).</summary>
public sealed record PlacasAtivadas(string Destino, IReadOnlyList<PlacaParaAtivar> Placas);

/// <summary>
/// A ativação da placa pelo próprio comprador, em /ativar, só com o código do grupo: sem login, sem cobrança e sem vendedor.
/// Roda no escopo da revenda dona das placas, que a rota pública assume depois de achar o grupo pelo código. Lotes antigos
/// continuam usando o código de lote; grupos novos ativam somente as placas separadas para aquele pedido.
/// </summary>
public sealed class AtivacaoPeloComprador(
    IBancoDeDados banco,
    IContas contas,
    Auditoria.Auditoria auditoria,
    ResolvedorDeDestino resolvedor,
    Links.DestinosBloqueados destinosBloqueados,
    IOptions<OpcoesDaPlataforma> plataforma,
    TimeProvider relogio)
{
    public const string ProdutoDaVendaDigital = "Placa da venda digital";

    private const string NaoEncontrado = "Código não encontrado. Confira o código que veio com as placas.";

    /// <summary>
    /// O lote do código, lido sem revenda (a tabela de lotes é da plataforma). Nulo para código errado, lote arquivado ou
    /// lote que não teve a baixa da venda digital: tudo responde igual, como código inexistente.
    /// </summary>
    public static async Task<(Guid LoteId, Guid RevendedorId)?> AcharLote(IBancoDeDados semRevenda, string? codigo, CancellationToken ct)
    {
        var normalizado = CodigoDaPlaca.Normalizar(codigo);
        if (normalizado.Length == 0)
        {
            return null;
        }

        var grupo = await semRevenda.PlacasEmitidas.AsNoTracking()
            .Where(p => p.CodigoDeAtivacao == normalizado && p.GrupoDeAtivacaoId != null && p.RevendedorId != null)
            .Select(p => new { p.LoteId, p.RevendedorId, p.GrupoDeAtivacaoId })
            .FirstOrDefaultAsync(ct);
        if (grupo is not null && await semRevenda.Lotes.AnyAsync(l => l.Id == grupo.LoteId && l.ArquivadoEm == null, ct))
            return (grupo.LoteId, grupo.RevendedorId!.Value);

        var lote = await semRevenda.Lotes.AsNoTracking()
            .Where(l => l.CodigoDeAtivacao == normalizado && l.VendidoNoDigitalEm != null && l.ArquivadoEm == null && l.RevendedorId != null)
            .Select(l => new { l.Id, l.RevendedorId }).SingleOrDefaultAsync(ct);
        return lote is null ? null : (lote.Id, lote.RevendedorId!.Value);
    }

    public async Task<LoteParaAtivar> Ler(Guid loteId, string codigo, CancellationToken ct)
    {
        var (lote, grupo) = await Contexto(loteId, codigo, ct);
        var codigos = banco.PlacasEmitidas.AsNoTracking().Where(p => p.LoteId == loteId);
        if (grupo is not null) codigos = codigos.Where(p => p.GrupoDeAtivacaoId == grupo.Id);
        var placasDoAcesso = await codigos.Select(p => p.Codigo).ToListAsync(ct);
        var placas = await banco.Placas.AsNoTracking().Where(p => placasDoAcesso.Contains(p.Codigo) && p.Situacao != SituacaoDaPlaca.Baixada)
            .OrderBy(p => p.Sequencia).ToListAsync(ct);
        return new LoteParaAtivar(lote.Rotulo, Tipo(lote), placas.Select(Dto).ToList());
    }

    /// <summary>
    /// Primeira ativação: cliente achado pelo telefone ou criado com a empresa e o telefone informados, uma venda por
    /// placa com a parte dela no valor do lote, e a placa no ar por 12 meses a partir de agora. No cardápio, todas as
    /// placas livres do lote recebem o mesmo link.
    /// </summary>
    public async Task<PlacasAtivadas> Ativar(Guid loteId, string codigo, AtivacaoDoComprador pedido, CancellationToken ct)
    {
        var (lote, grupo) = await Contexto(loteId, codigo, ct);
        var destino = await Destino(lote, pedido.Destino, ct);
        var agora = relogio.GetUtcNow();

        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        _ = await banco.TravarLotePorId(lote.Id, ct) ?? throw new RegraDeNegocioException(NaoEncontrado);
        if (grupo is not null && await banco.GruposDeAtivacao.AnyAsync(g => g.Id == grupo.Id && g.CanceladoEm != null, ct))
            throw new RegraDeNegocioException(NaoEncontrado);
        var placas = await Alvos(lote, grupo, pedido.CodigoDaPlaca, ct);
        var livres = placas.Where(p => p.Situacao == SituacaoDaPlaca.EmEstoque && p.VendaId is null).ToList();
        if (livres.Count == 0)
        {
            throw new RegraDeNegocioException(lote.AtivaUmLinkParaTodas || placas.Count > 1
                ? "Placas já ativadas. Use Trocar destino."
                : "Placa já ativada. Use Trocar destino.");
        }

        var lugar = pedido.Destino.Tipo == TipoDeDestino.Google ? pedido.Destino.LugarNoGoogle : null;
        var cliente = await Cliente(pedido.Empresa, pedido.Telefone, lugar, agora, ct);
        var produto = await Produto(ct);
        var revendedorId = grupo?.RevendedorId ?? lote.RevendedorId!.Value;
        var dono = await Dono(revendedorId, ct);
        var emitidas = banco.PlacasEmitidas.AsNoTracking().Where(p => p.LoteId == lote.Id);
        if (grupo is not null) emitidas = emitidas.Where(p => p.GrupoDeAtivacaoId == grupo.Id);
        var posicoes = await emitidas.OrderBy(p => p.Sequencia)
            .Select(p => p.Codigo).ToListAsync(ct);
        foreach (var placa in livres)
        {
            var valor = grupo is null ? lote.ValorDaPlacaNoDigital(posicoes.IndexOf(placa.Codigo)) : grupo.ValorDaPlaca(posicoes.IndexOf(placa.Codigo), posicoes.Count);
            var venda = Venda.Digital(placa.Id, cliente.Id, produto.Id, dono, valor, placa.CustoCents, agora);
            placa.Vender(venda.Id, destino, agora);
            banco.Vendas.Add(venda);
            auditoria.Registrar(AcaoAuditada.DestinoDefinido, $"Placa {placa.Codigo}", placa.Codigo, null, destino);
        }

        banco.AvisarAoVivo("venda", revendedorId);
        await banco.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
        return new PlacasAtivadas(destino, livres.Select(Dto).ToList());
    }

    /// <summary>A placa já ativada troca de destino só com o código do lote; no cardápio, todas as do lote juntas.</summary>
    public async Task<PlacasAtivadas> TrocarDestino(Guid loteId, string codigo, TrocaDeDestinoDoComprador pedido, CancellationToken ct)
    {
        var (lote, grupo) = await Contexto(loteId, codigo, ct);
        var destino = await Destino(lote, pedido.Destino, ct);

        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        _ = await banco.TravarLotePorId(lote.Id, ct) ?? throw new RegraDeNegocioException(NaoEncontrado);
        if (grupo is not null && await banco.GruposDeAtivacao.AnyAsync(g => g.Id == grupo.Id && g.CanceladoEm != null, ct))
            throw new RegraDeNegocioException(NaoEncontrado);
        var ativas = (await Alvos(lote, grupo, pedido.CodigoDaPlaca, ct)).Where(p => p.VendaId is not null).ToList();
        if (ativas.Count == 0)
        {
            throw new RegraDeNegocioException("Placa ainda não ativada. Ative antes de trocar o destino.");
        }

        foreach (var placa in ativas)
        {
            var antes = placa.Destino;
            if (placa.Apontar(destino))
            {
                auditoria.Registrar(AcaoAuditada.DestinoTrocado, $"Placa {placa.Codigo}", placa.Codigo, antes, destino);
            }
        }

        await banco.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
        return new PlacasAtivadas(destino, ativas.Select(Dto).ToList());
    }

    private static TipoDeAtivacao Tipo(Lote lote) => lote.AtivaUmLinkParaTodas ? TipoDeAtivacao.Cardapio : TipoDeAtivacao.Avaliacao;

    private PlacaParaAtivar Dto(Placa p) => new(p.Codigo, p.VendaId is not null, p.Destino, plataforma.Value.EnderecoDaPlaca(p.Codigo));

    private async Task<Lote> Lote(Guid loteId, CancellationToken ct) =>
        await banco.Lotes.AsNoTracking().SingleOrDefaultAsync(l => l.Id == loteId && l.VendidoNoDigitalEm != null && l.ArquivadoEm == null, ct)
        ?? throw new RegraDeNegocioException(NaoEncontrado);

    /// <summary>Avaliação só aceita o Google; cardápio só aceita link. As mesmas regras de destino da venda no balcão.</summary>
    private async Task<string> Destino(Lote lote, DestinoInformado informado, CancellationToken ct)
    {
        if (lote.AtivaUmLinkParaTodas && informado.Tipo != TipoDeDestino.Link)
        {
            throw new RegraDeNegocioException("Cole o link do cardápio digital.");
        }

        if (!lote.AtivaUmLinkParaTodas && informado.Tipo != TipoDeDestino.Google)
        {
            throw new RegraDeNegocioException("Busque o estabelecimento no Google ou cole o link de avaliação.");
        }

        var destino = await resolvedor.Resolver(informado, ct);
        await destinosBloqueados.ExigirPermitidos([destino], ct);
        return destino;
    }

    /// <summary>
    /// As placas travadas que a ação alcança: no cardápio, todas as do lote que não saíram de circulação; na avaliação,
    /// a do código informado, ou a única do lote. Código de outro lote responde como inexistente.
    /// </summary>
    private async Task<List<Placa>> Alvos(Lote lote, GrupoDeAtivacao? grupo, string? codigoDaPlaca, CancellationToken ct)
    {
        var emitidas = banco.PlacasEmitidas.AsNoTracking().Where(p => p.LoteId == lote.Id);
        if (grupo is not null) emitidas = emitidas.Where(p => p.GrupoDeAtivacaoId == grupo.Id);
        var doLote = await emitidas.OrderBy(p => p.Sequencia)
            .Select(p => p.Codigo).ToListAsync(ct);
        IReadOnlyCollection<string> codigos;
        if (lote.AtivaUmLinkParaTodas || doLote.Count == 1)
        {
            codigos = doLote;
        }
        else
        {
            var codigo = CodigoDaPlaca.Normalizar(codigoDaPlaca);
            if (codigo.Length == 0)
            {
                throw new RegraDeNegocioException("Informe o código impresso na placa.");
            }

            codigos = doLote.Contains(codigo) ? [codigo] : throw new RegraDeNegocioException("Placa fora deste lote. Confira o código impresso na placa.");
        }

        var placas = await banco.TravarPlacas(codigos, ct);
        return placas.Where(p => p.Situacao != SituacaoDaPlaca.Baixada).OrderBy(p => p.Sequencia).ToList();
    }

    private async Task<(Lote Lote, GrupoDeAtivacao? Grupo)> Contexto(Guid loteId, string codigo, CancellationToken ct)
    {
        var normalizado = CodigoDaPlaca.Normalizar(codigo);
        var grupo = await banco.GruposDeAtivacao.AsNoTracking().SingleOrDefaultAsync(g => g.Codigo == normalizado && g.CanceladoEm == null, ct);
        var lote = grupo is not null
            ? await banco.Lotes.AsNoTracking().SingleOrDefaultAsync(l => l.Id == grupo.LoteId && l.ArquivadoEm == null, ct)
            : await Lote(loteId, ct);
        if (lote is null || lote.Id != loteId || grupo is null && lote.VendidoNoDigitalEm is null)
            throw new RegraDeNegocioException(NaoEncontrado);
        return (lote, grupo);
    }

    /// <summary>O mesmo cliente para o mesmo telefone sem documento; a empresa é também o responsável, porque o comprador só informa os dois.</summary>
    private async Task<Cliente> Cliente(string empresa, string telefone, string? lugarNoGoogle, DateTimeOffset agora, CancellationToken ct)
    {
        var novo = new Cliente(empresa, empresa, telefone, null, agora);
        var existente = await banco.Clientes.Where(c => c.Telefone == novo.Telefone && c.Documento == null && c.AnonimizadoEm == null)
            .OrderBy(c => c.CriadoEm).ThenBy(c => c.Id).FirstOrDefaultAsync(ct);
        var cliente = existente ?? novo;
        if (existente is null)
        {
            banco.Clientes.Add(novo);
        }

        cliente.Completar(null, null, lugarNoGoogle);
        return cliente;
    }

    private async Task<Produto> Produto(CancellationToken ct)
    {
        var produto = await banco.Produtos.FirstOrDefaultAsync(p => p.Nome == ProdutoDaVendaDigital && p.ProdutoDaPlataformaId == null, ct);
        if (produto is null)
        {
            produto = new Produto(ProdutoDaVendaDigital, 0);
            banco.Produtos.Add(produto);
        }

        return produto;
    }

    /// <summary>A venda fica no nome do dono, que fez a baixa do lote pela operadora: não há vendedor nem comissão.</summary>
    private async Task<Guid> Dono(Guid revendedorId, CancellationToken ct)
    {
        foreach (var email in await contas.EmailsDosDonos(revendedorId, ct))
        {
            if (await contas.IdPorEmail(email, ct) is { } id)
            {
                return id;
            }
        }

        throw new RegraDeNegocioException("Revenda sem dono para registrar a ativação. Fale com a loja.");
    }
}
