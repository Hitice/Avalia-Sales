using System.Globalization;
using AvaliaSales.Application.Avisos;
using AvaliaSales.Application.Equipe;
using AvaliaSales.Domain;
using AvaliaSales.Domain.Pedidos;
using AvaliaSales.Domain.Placas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Application.Pedidos;

public sealed record PedidoDto(
    Guid Id, Guid RevendedorId, string Revendedor, int Quantidade, long ValorCents, SituacaoDoPedido Situacao,
    DateTimeOffset CriadoEm, DateTimeOffset? PagoEm, string? Lote, DateTimeOffset? DespachadoEm, DateTimeOffset? AtivadoEm, DateTimeOffset? ProducaoEm,
    long PrecoDoPacoteCents, string? Cupom, long DescontoCents, string? UrlDePagamento, bool Retirada,
    bool HaLoteLivre = false);

public sealed record AtivacaoDto(string Lote, int Placas, string PrimeiroCodigo, string UltimoCodigo);

/// <summary>
/// Do pedido à ativação: o revendedor pede um pacote, a operadora registra o
/// pagamento e despacha um lote da mesma quantidade, e o revendedor ativa o
/// lote pelo número da caixa. O revendedor paga pelo checkout do Pagar.me;
/// a operadora também pode registrar um pagamento recebido por fora.
/// </summary>
public sealed class PedidosDePlacas(
    IBancoDeDados banco,
    IRevendedorAtual atual,
    IContas contas,
    FilaDeEmails avisos,
    Pagamentos.Cobrancas cobrancas,
    Erp.FilaDoErp erp,
    Notificacoes.Notificacoes notificacoes,
    IOptions<OpcoesDaPlataforma> plataforma,
    TimeProvider relogio)
{
    /// <summary>
    /// A página dos pedidos, do mais novo ao mais antigo. Com <paramref name="busca"/>, o texto no nome do revendedor,
    /// no cupom ou no número do lote. <see cref="PedidoDto.HaLoteLivre"/> diz se a operadora tem lote gerado e ainda
    /// não despachado com a quantidade do pedido.
    /// </summary>
    public async Task<Pagina<PedidoDto>> Listar(string? busca, PedidoDePagina pedido, CancellationToken ct)
    {
        var consulta = banco.Pedidos.AsNoTracking();
        var termo = Paginacao.Termo(busca);
        if (termo.Length > 0)
        {
            int? numero = int.TryParse(new string(termo.Where(char.IsAsciiDigit).ToArray()), out var lido) ? lido : null;
            consulta = consulta.Where(p => banco.Revendedores.Any(r => r.Id == p.RevendedorId && Busca.Normalizar(r.Nome).Contains(Busca.Normalizar(termo)))
                || (p.Cupom != null && Busca.Normalizar(p.Cupom).Contains(Busca.Normalizar(termo)))
                || (numero != null && banco.Lotes.Any(l => l.Id == p.LoteId && l.Numero == numero)));
        }

        var pagina = await consulta.OrderByDescending(p => p.CriadoEm).ThenByDescending(p => p.Id).PaginarAsync(pedido, ct);
        return new Pagina<PedidoDto>(await Dtos(pagina.Itens, ct), pagina.Total, pagina.Proxima);
    }

    private async Task<List<PedidoDto>> Dtos(IReadOnlyList<PedidoDePlacas> pedidos, CancellationToken ct)
    {
        var revendedores = await banco.Revendedores.AsNoTracking()
            .Where(r => pedidos.Select(p => p.RevendedorId).Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Nome, ct);
        var lotes = await banco.Lotes.AsNoTracking()
            .Where(l => pedidos.Select(p => p.LoteId).Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.Numero, ct);
        var ids = pedidos.Select(p => (Guid?)p.Id).ToList();

        // O link em aberto mais recente de cada pedido. A operadora não lê cobranças (RLS): para ela, sai nulo.
        var links = await banco.Cobrancas.AsNoTracking()
            .Where(c => c.Origem == Domain.Pagamentos.OrigemDaCobranca.PedidoDePlacas && ids.Contains(c.OrigemId) && c.Situacao == Domain.Pagamentos.SituacaoDaCobranca.Pendente && c.UrlDoLink != null)
            .Select(c => new { c.OrigemId, c.UrlDoLink, c.CriadaEm })
            .ToListAsync(ct);
        var linkDe = links.GroupBy(l => l.OrigemId!.Value).ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.CriadaEm).First().UrlDoLink);
        var quantidades = pedidos.Where(p => p.Situacao is SituacaoDoPedido.Pago or SituacaoDoPedido.EmProducao).Select(p => p.Quantidade).Distinct().ToList();
        var comLoteLivre = await banco.Lotes.AsNoTracking()
            .Where(l => l.DespachadoEm == null && l.ArquivadoEm == null && quantidades.Contains(l.Quantidade))
            .Select(l => l.Quantidade).Distinct().ToListAsync(ct);
        return pedidos.Select(p => new PedidoDto(
            p.Id, p.RevendedorId, revendedores.GetValueOrDefault(p.RevendedorId, ""), p.Quantidade, p.ValorCents, p.Situacao,
            p.CriadoEm, p.PagoEm, p.LoteId is { } l && lotes.TryGetValue(l, out var n) ? Lote.Rotular(n) : null,
            p.DespachadoEm, p.AtivadoEm, p.ProducaoEm, p.PrecoDoPacoteCents, p.Cupom, p.DescontoCents, linkDe.GetValueOrDefault(p.Id), p.Retirada,
            comLoteLivre.Contains(p.Quantidade))).ToList();
    }

    /// <summary>Sem contrato de revenda assinado, não há pedido. O cupom é conferido e gasto aqui.</summary>
    public async Task<PedidoDto> Criar(Guid pacoteId, string? codigoDoCupom, CancellationToken ct)
    {
        (await banco.Revendedores.SingleAsync(r => r.Id == atual.RevendedorId, ct)).ExigirAtiva();
        var revendedor = await banco.Revendedores.SingleAsync(r => r.Id == atual.RevendedorId, ct);
        if (revendedor.ContratoAssinadoEm is null)
        {
            throw new RegraDeNegocioException("Assine o contrato de revenda antes do primeiro pedido.");
        }

        var pacote = await banco.Pacotes.SingleOrDefaultAsync(p => p.Id == pacoteId, ct)
            ?? throw new RegraDeNegocioException("Pacote não encontrado.");
        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        Cupom? cupom = null;
        if (!string.IsNullOrWhiteSpace(codigoDoCupom))
        {
            cupom = await banco.TravarCupom(Cupom.Normalizar(codigoDoCupom), ct)
                ?? throw new RegraDeNegocioException("Cupom não encontrado.");
        }

        var agora = relogio.GetUtcNow();
        var pedido = new PedidoDePlacas(pacote, agora, cupom);
        banco.Pedidos.Add(pedido);
        if (pedido.Gratuito)
        {
            pedido.RegistrarPagamento(agora);
            await erp.Enfileirar(pedido, revendedor.Id, ct);
        }

        banco.AvisarAoVivo("pedido", revendedor.Id, operadora: true);
        notificacoes.ParaOperadora(revendedor.Id, "Pedido novo", $"{revendedor.Nome} pediu {pedido.Quantidade} placas.", "/operadora/pedidos");

        var site = plataforma.Value;
        avisos.Enfileirar($"pedido:{pedido.Id:N}:operadora", site.EmailDaOperadora,
            $"Pedido de {pedido.Quantidade} placas - {revendedor.Nome}",
            $"""
            {revendedor.Nome} pediu {pedido.Quantidade} placas, {Reais(pedido.ValorCents)}.

            Registre o pagamento e despache um lote de {pedido.Quantidade} placas.

            Abrir pedidos: {site.UrlDoPainel.TrimEnd('/')}/operadora/pedidos
            """);
        await banco.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
        return (await Dtos([pedido], ct))[0];
    }

    /// <summary>
    /// O link do checkout do Pagar.me para pagar o pedido. Chamar de novo
    /// devolve o link em aberto, e só cria outro se o anterior venceu ou falhou.
    /// </summary>
    public async Task<string> Pagar(Guid id, CancellationToken ct)
    {
        var pedido = await Achar(id, ct);
        if (pedido.Situacao != SituacaoDoPedido.AguardandoPagamento)
        {
            throw new RegraDeNegocioException("Este pedido não está aguardando pagamento.");
        }

        var aberto = await banco.Cobrancas.AsNoTracking()
            .Where(c => c.Origem == Domain.Pagamentos.OrigemDaCobranca.PedidoDePlacas && c.OrigemId == id && c.Situacao == Domain.Pagamentos.SituacaoDaCobranca.Pendente && c.UrlDoLink != null)
            .OrderByDescending(c => c.CriadaEm).Select(c => c.UrlDoLink).FirstOrDefaultAsync(ct);
        if (aberto is not null)
        {
            return aberto;
        }

        var cobranca = await cobrancas.CriarLink(Domain.Pagamentos.OrigemDaCobranca.PedidoDePlacas, id, pedido.ValorCents, $"Pedido de {pedido.Quantidade} placas", ct);
        return cobranca.UrlDoLink!;
    }

    public async Task Cancelar(Guid id, CancellationToken ct)
    {
        var pedido = await Achar(id, ct);

        // Link em aberto ainda pode ser pago: cancelar agora deixaria o dinheiro sem pedido.
        if (await banco.Cobrancas.AnyAsync(c => c.Origem == Domain.Pagamentos.OrigemDaCobranca.PedidoDePlacas && c.OrigemId == id
                && c.Situacao == Domain.Pagamentos.SituacaoDaCobranca.Pendente && c.UrlDoLink != null, ct))
        {
            throw new RegraDeNegocioException("Pedido com link de pagamento em aberto.");
        }

        pedido.Cancelar();
        if (pedido.Cupom is { } codigo && await banco.Cupons.SingleOrDefaultAsync(c => c.Codigo == codigo, ct) is { } cupom)
        {
            cupom.Devolver();
        }

        await banco.SaveChangesAsync(ct);
    }

    /// <summary>Operadora: pagamento recebido por fora, até o checkout do Pagar.me entrar.</summary>
    public async Task RegistrarPagamento(Guid id, CancellationToken ct)
    {
        var pedido = await Achar(id, ct);
        pedido.RegistrarPagamento(relogio.GetUtcNow());
        banco.AvisarAoVivo("pedido", pedido.RevendedorId, operadora: true);
        await erp.Enfileirar(pedido, pedido.RevendedorId, ct);
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>Operadora: as placas do pedido pago entraram em produção.</summary>
    public async Task IniciarProducao(Guid id, CancellationToken ct)
    {
        var pedido = await Achar(id, ct);
        pedido.IniciarProducao(relogio.GetUtcNow());
        banco.AvisarAoVivo("pedido", pedido.RevendedorId, operadora: true);
        notificacoes.ParaDono(pedido.RevendedorId, "Pedido em produção", $"As suas {pedido.Quantidade} placas estão sendo produzidas.", "/painel/pedidos");
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Operadora: liga um lote livre da mesma quantidade ao pedido e avisa o
    /// dono, que recebe pelo envio ou retira a caixa na Avalia.
    /// </summary>
    public async Task Despachar(Guid id, int numeroDoLote, bool retirada, CancellationToken ct)
    {
        var pedido = await Achar(id, ct);
        var lote = await banco.Lotes.SingleOrDefaultAsync(l => l.Numero == numeroDoLote, ct)
            ?? throw new RegraDeNegocioException($"O lote {Lote.Rotular(numeroDoLote)} não existe.");
        var agora = relogio.GetUtcNow();

        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        lote.Despachar(pedido.Id, pedido.RevendedorId, pedido.Quantidade, agora);
        pedido.Despachar(lote.Id, agora, retirada);
        banco.AvisarAoVivo("pedido", pedido.RevendedorId, operadora: true);
        var (titulo, texto, assunto, corpo) = retirada
            ? ("Placas prontas para retirada", $"O lote {lote.Rotulo} com {pedido.Quantidade} placas está pronto para retirada na Avalia. Ative pelo número da caixa.",
                $"Suas {pedido.Quantidade} placas estão prontas para retirada - lote {lote.Rotulo}",
                $"O lote {lote.Rotulo} com {pedido.Quantidade} placas está pronto para retirada na Avalia.\n\nAo retirar a caixa, ative o lote pelo número impresso nela.")
            : ("Placas enviadas", $"O lote {lote.Rotulo} com {pedido.Quantidade} placas saiu. Ative pelo número da caixa.",
                $"Suas {pedido.Quantidade} placas saíram - lote {lote.Rotulo}",
                $"O lote {lote.Rotulo} com {pedido.Quantidade} placas foi despachado.\n\nQuando a caixa chegar, ative o lote pelo número impresso nela.");
        notificacoes.ParaDono(pedido.RevendedorId, titulo, texto, "/painel/estoque");
        foreach (var dono in await contas.EmailsDosDonos(pedido.RevendedorId, ct))
        {
            avisos.Enfileirar($"pedido:{pedido.Id:N}:despachado:{dono}", dono, assunto,
                $"""
                {corpo}

                Ativar o lote: {plataforma.Value.UrlDoPainel.TrimEnd('/')}/painel/estoque
                """);
        }

        await banco.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
    }

    /// <summary>
    /// O revendedor digita o número da caixa e as placas entram no estoque
    /// dele, com o custo do pacote congelado em cada uma.
    /// </summary>
    public async Task<AtivacaoDto> Ativar(string numeroDigitado, CancellationToken ct)
    {
        var revendedorId = atual.RevendedorId ?? throw new InvalidOperationException("Ativação sem revendedor.");
        var numero = Lote.LerNumero(numeroDigitado) ?? throw new RegraDeNegocioException("Digite o número do lote, como LT-0001.");
        var agora = relogio.GetUtcNow();

        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        var lote = await banco.TravarLote(numero, ct)
            ?? throw new RegraDeNegocioException("Lote não encontrado. Confira o número na caixa.");
        lote.Ativar(revendedorId, agora);
        var pedido = await banco.Pedidos.SingleAsync(p => p.Id == lote.PedidoId, ct);
        pedido.Ativar(agora);
        banco.AvisarAoVivo("estoque", revendedorId, operadora: true);
        notificacoes.ParaOperadora(revendedorId, "Pedido entregue", $"O lote {lote.Rotulo} foi ativado pela revenda.", "/operadora/pedidos");

        var emitidas = await banco.PlacasEmitidas.Where(p => p.LoteId == lote.Id).OrderBy(p => p.Sequencia).ToListAsync(ct);
        foreach (var emitida in emitidas)
        {
            emitida.EntregarA(revendedorId);
            banco.Placas.Add(new Placa(emitida, pedido.CustoPorPlacaCents, agora));
        }

        await banco.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
        return new AtivacaoDto(lote.Rotulo, emitidas.Count, emitidas[0].Codigo, emitidas[^1].Codigo);
    }

    private async Task<PedidoDePlacas> Achar(Guid id, CancellationToken ct) =>
        await banco.Pedidos.SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw new RegraDeNegocioException("Pedido não encontrado.");

    private static string Reais(long centavos) =>
        (centavos / 100m).ToString("C", CultureInfo.GetCultureInfo("pt-BR"));
}
