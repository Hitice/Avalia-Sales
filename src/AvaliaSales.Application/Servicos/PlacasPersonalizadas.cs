using AvaliaSales.Application.Placas;
using AvaliaSales.Domain;
using AvaliaSales.Domain.Placas;
using AvaliaSales.Domain.Servicos;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Servicos;

/// <summary>Os códigos gerados, na ordem dos destinos da venda: o primeiro é o principal.</summary>
public sealed record CodigosDaPlaca(string Lote, IReadOnlyList<string> Codigos);

/// <summary>
/// A placa personalizada na ordem de serviço (ADR 0032). A venda paga traz os destinos; a operadora gera os códigos,
/// que nascem já vendidos no estoque da revenda e no ar para conferir a arte, e a entrega começa os 12 meses.
/// </summary>
public sealed class PlacasPersonalizadas(IEscopoDaOrdem escopo, TimeProvider relogio)
{
    /// <summary>Um lote só desta placa, da revenda que vendeu, com um código por destino.</summary>
    public Task<CodigosDaPlaca> GerarCodigos(Guid ordemId, CancellationToken ct) => escopo.Gravar(ordemId, async (banco, ordem) =>
    {
        if (ordem.Situacao is not (SituacaoDaOrdem.AFazer or SituacaoDaOrdem.EmAndamento))
        {
            throw new RegraDeNegocioException("Ordem já encerrada.");
        }

        if (ordem.CodigosNaPlaca == 0)
        {
            throw new RegraDeNegocioException("Ordem sem placa personalizada.");
        }

        if (await banco.Revendedores.AnyAsync(r => r.Id == ordem.RevendedorId && (r.EncerradaEm != null || r.EncerramentoPedidoEm != null), ct))
        {
            throw new RegraDeNegocioException("Revenda encerrada ou em encerramento.");
        }

        // A mesma trava do cancelamento da venda: os códigos não nascem numa venda que está sendo cancelada.
        var cobradaPor = await banco.Vendas.AsNoTracking().Where(v => v.Id == ordem.VendaId).Select(v => v.GrupoId ?? v.Id).SingleAsync(ct);
        await banco.TravarOrigemDaCobranca(cobradaPor, ct);
        var venda = await banco.Vendas.SingleAsync(v => v.Id == ordem.VendaId, ct);
        if (venda.Situacao == Domain.Vendas.SituacaoDaVenda.Cancelada)
        {
            throw new RegraDeNegocioException("Venda cancelada.");
        }

        if (ordem.LoteId is not null || venda.PlacaId is not null)
        {
            throw new RegraDeNegocioException("Códigos da placa já gerados.");
        }

        var destinos = await banco.DestinosDaVenda.Where(d => d.VendaId == venda.Id).OrderBy(d => d.Posicao).Select(d => d.Destino).ToListAsync(ct);
        if (destinos.Count == 0)
        {
            throw new RegraDeNegocioException("Venda sem os destinos da placa.");
        }

        await banco.TravarGeracaoDeLotes(ct);
        var agora = relogio.GetUtcNow();
        var numero = (await banco.Lotes.MaxAsync(l => (int?)l.Numero, ct) ?? 0) + 1;
        var titulo = $"Placa personalizada, ordem {ordem.Numero}, {ordem.Cliente}";
        var lote = new Lote(numero, destinos.Count, agora, titulo.Length <= Lote.TituloMaximo ? titulo : titulo[..Lote.TituloMaximo], CategoriaDoLote.Outro);
        lote.EntregarParaOrdem(ordem.RevendedorId, agora);
        banco.Lotes.Add(lote);
        var sequencia = await banco.PlacasEmitidas.MaxAsync(p => (long?)p.Sequencia, ct) ?? 0;
        var codigos = await Lotes.SortearLivres(banco, destinos.Count, ct);

        Placa? principal = null;
        foreach (var (codigo, destino) in codigos.Zip(destinos))
        {
            var emitida = new PlacaEmitida(codigo, ++sequencia, lote.Id);
            emitida.EntregarA(ordem.RevendedorId);
            banco.PlacasEmitidas.Add(emitida);
            // Sem custo: a placa personalizada é serviço, e a produção é da Avalia.
            var placa = new Placa(emitida, 0, agora);
            placa.Vender(venda.Id, destino, agora);
            if (principal is null)
            {
                principal = placa;
            }
            else
            {
                placa.SerAdicionalDe(principal);
            }

            banco.Placas.Add(placa);
        }

        venda.LigarPlacaPersonalizada(principal!.Id);
        ordem.LigarLote(lote.Id);
        if (ordem.Situacao == SituacaoDaOrdem.AFazer)
        {
            ordem.Iniciar(agora);
        }

        return new CodigosDaPlaca(lote.Rotulo, codigos);
    }, ct);

    /// <summary>A placa chegou ao cliente: a ordem fecha e os 12 meses de todos os códigos contam daqui.</summary>
    public Task Entregar(Guid ordemId, CancellationToken ct) => escopo.Gravar(ordemId, async (banco, ordem) =>
    {
        if (ordem.LoteId is null)
        {
            throw new RegraDeNegocioException("Placa sem códigos. Gere os códigos antes de entregar.");
        }

        var agora = relogio.GetUtcNow();
        ordem.Entregar(agora);
        foreach (var placa in await banco.Placas.Where(p => p.VendaId == ordem.VendaId && p.Situacao != SituacaoDaPlaca.Baixada).ToListAsync(ct))
        {
            placa.ComecarVigenciaNaEntrega(agora);
        }

        return true;
    }, ct);
}
