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
    DateTimeOffset CriadoEm, DateTimeOffset? PagoEm, string? Lote, DateTimeOffset? DespachadoEm, DateTimeOffset? AtivadoEm);

public sealed record AtivacaoDto(string Lote, int Placas, string PrimeiroCodigo, string UltimoCodigo);

/// <summary>
/// Do pedido à ativação: o revendedor pede um pacote, a operadora registra o
/// pagamento e despacha um lote da mesma quantidade, e o revendedor ativa o
/// lote pelo número da caixa. O pagamento pelo Pagar.me entra na Fase 2; até
/// lá a operadora registra o pagamento recebido.
/// </summary>
public sealed class PedidosDePlacas(
    IBancoDeDados banco,
    IRevendedorAtual atual,
    IContas contas,
    FilaDeEmails avisos,
    IOptions<OpcoesDaPlataforma> plataforma,
    TimeProvider relogio)
{
    public async Task<IReadOnlyList<PedidoDto>> Listar(CancellationToken ct)
    {
        var pedidos = await banco.Pedidos.AsNoTracking().OrderByDescending(p => p.CriadoEm).ToListAsync(ct);
        var revendedores = await banco.Revendedores.AsNoTracking()
            .Where(r => pedidos.Select(p => p.RevendedorId).Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Nome, ct);
        var lotes = await banco.Lotes.AsNoTracking()
            .Where(l => pedidos.Select(p => p.LoteId).Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.Numero, ct);
        return pedidos.Select(p => new PedidoDto(
            p.Id, p.RevendedorId, revendedores.GetValueOrDefault(p.RevendedorId, ""), p.Quantidade, p.ValorCents, p.Situacao,
            p.CriadoEm, p.PagoEm, p.LoteId is { } l && lotes.TryGetValue(l, out var n) ? Lote.Rotular(n) : null,
            p.DespachadoEm, p.AtivadoEm)).ToList();
    }

    /// <summary>Sem contrato de revenda assinado, não há pedido.</summary>
    public async Task<PedidoDto> Criar(Guid pacoteId, CancellationToken ct)
    {
        var revendedor = await banco.Revendedores.SingleAsync(r => r.Id == atual.RevendedorId, ct);
        if (revendedor.ContratoAssinadoEm is null)
        {
            throw new RegraDeNegocioException("Assine o contrato de revenda antes do primeiro pedido.");
        }

        var pacote = await banco.Pacotes.SingleOrDefaultAsync(p => p.Id == pacoteId, ct)
            ?? throw new RegraDeNegocioException("Pacote não encontrado.");
        var pedido = new PedidoDePlacas(pacote, relogio.GetUtcNow());
        banco.Pedidos.Add(pedido);

        var site = plataforma.Value;
        avisos.Enfileirar($"pedido:{pedido.Id:N}:operadora", site.EmailDaOperadora,
            $"Pedido de {pedido.Quantidade} placas - {revendedor.Nome}",
            $"""
            {revendedor.Nome} pediu {pedido.Quantidade} placas, {Reais(pedido.ValorCents)}.

            Registre o pagamento e despache um lote de {pedido.Quantidade} placas:
            {site.UrlDoPainel.TrimEnd('/')}/operadora/pedidos
            """);
        await banco.SaveChangesAsync(ct);
        return (await Listar(ct)).Single(p => p.Id == pedido.Id);
    }

    public async Task Cancelar(Guid id, CancellationToken ct)
    {
        (await Achar(id, ct)).Cancelar();
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>Operadora: pagamento recebido por fora, até o checkout do Pagar.me entrar.</summary>
    public async Task RegistrarPagamento(Guid id, CancellationToken ct)
    {
        (await Achar(id, ct)).RegistrarPagamento(relogio.GetUtcNow());
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>Operadora: liga um lote livre da mesma quantidade ao pedido e avisa o dono.</summary>
    public async Task Despachar(Guid id, int numeroDoLote, CancellationToken ct)
    {
        var pedido = await Achar(id, ct);
        var lote = await banco.Lotes.SingleOrDefaultAsync(l => l.Numero == numeroDoLote, ct)
            ?? throw new RegraDeNegocioException($"O lote {Lote.Rotular(numeroDoLote)} não existe.");
        var agora = relogio.GetUtcNow();

        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        lote.Despachar(pedido.Id, pedido.RevendedorId, pedido.Quantidade, agora);
        pedido.Despachar(lote.Id, agora);
        foreach (var dono in await contas.EmailsDosDonos(pedido.RevendedorId, ct))
        {
            avisos.Enfileirar($"pedido:{pedido.Id:N}:despachado:{dono}", dono, $"Suas {pedido.Quantidade} placas saíram - lote {lote.Rotulo}",
                $"""
                O lote {lote.Rotulo} com {pedido.Quantidade} placas foi despachado.

                Quando a caixa chegar, ative o lote pelo número impresso nela:
                {plataforma.Value.UrlDoPainel.TrimEnd('/')}/painel/estoque
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
