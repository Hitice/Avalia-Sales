using AvaliaSales.Application.Avisos;
using AvaliaSales.Domain.Financeiro;
using AvaliaSales.Domain.Pagamentos;
using AvaliaSales.Domain.Pedidos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Application.Pagamentos;

/// <summary>
/// O que muda no negócio quando a cobrança muda: pedido de placas pago, razão
/// da venda, renovação estendendo a placa, estorno desfazendo o razão. Roda
/// antes do SaveChanges de quem aplicou a cobrança: efeito e situação gravam
/// juntos, e um evento repetido não acha nada a fazer.
/// </summary>
public sealed class EfeitosDaCobranca(IBancoDeDados banco, FilaDeEmails avisos, IOptions<OpcoesDaPlataforma> plataforma)
{
    public async Task Aplicar(Cobranca cobranca, DateTimeOffset agora, CancellationToken ct)
    {
        switch (cobranca.Origem, cobranca.Situacao)
        {
            case (OrigemDaCobranca.PedidoDePlacas, SituacaoDaCobranca.Paga):
                await PedidoPago(cobranca, agora, ct);
                break;
            case (OrigemDaCobranca.Venda, SituacaoDaCobranca.Paga):
                await VendaPaga(cobranca, ct);
                break;
            case (OrigemDaCobranca.Renovacao, SituacaoDaCobranca.Paga):
                await RenovacaoPaga(cobranca, ct);
                break;
            case (OrigemDaCobranca.Venda or OrigemDaCobranca.Renovacao, SituacaoDaCobranca.Estornada):
                await Estornar(cobranca, agora, ct);
                break;
        }
    }

    private async Task PedidoPago(Cobranca cobranca, DateTimeOffset agora, CancellationToken ct)
    {
        var pedido = await banco.Pedidos.SingleOrDefaultAsync(p => p.Id == cobranca.OrigemId, ct);
        if (pedido is null)
        {
            return;
        }

        if (pedido.Situacao != SituacaoDoPedido.AguardandoPagamento)
        {
            // Pago por fora antes, ou cancelado: o dinheiro entrou duas vezes ou sem pedido, e a operadora devolve.
            avisos.Enfileirar($"pedido:{pedido.Id:N}:pago-de-novo:{cobranca.Id:N}", plataforma.Value.EmailDaOperadora,
                $"Pagamento a devolver - pedido de {pedido.Quantidade} placas",
                $"O pedido {pedido.Id} está {pedido.Situacao} e recebeu o pagamento {cobranca.PedidoNoGateway} pelo Pagar.me. Estorne pelo painel do Pagar.me.");
            return;
        }

        pedido.RegistrarPagamento(cobranca.PagaEm ?? agora);
        var revendedor = await banco.Revendedores.Where(r => r.Id == pedido.RevendedorId).Select(r => r.Nome).SingleAsync(ct);
        var site = plataforma.Value;
        avisos.Enfileirar($"pedido:{pedido.Id:N}:pago", site.EmailDaOperadora, $"Pedido pago - {pedido.Quantidade} placas - {revendedor}",
            $"""
            {revendedor} pagou o pedido de {pedido.Quantidade} placas pelo Pagar.me.

            Despache um lote de {pedido.Quantidade} placas:
            {site.UrlDoPainel.TrimEnd('/')}/operadora/pedidos
            """);
    }

    /// <summary>Receita bruta, taxa da Avalia retida no split e a comissão que passa a ser devida.</summary>
    private async Task VendaPaga(Cobranca cobranca, CancellationToken ct)
    {
        var venda = await banco.Vendas.SingleOrDefaultAsync(v => v.Id == cobranca.OrigemId, ct);
        if (venda is null || await JaLancada(cobranca.Id, ct))
        {
            return;
        }

        var placa = await banco.Placas.Where(p => p.Id == venda.PlacaId).Select(p => p.Codigo).SingleAsync(ct);
        var quando = cobranca.PagaEm!.Value;
        banco.Lancamentos.Add(new Lancamento(TipoDeLancamento.Receita, cobranca.Id, cobranca.ValorCents, $"Venda da placa {placa}", quando, venda.Id, venda.VendedorId));
        if (cobranca.TaxaAvaliaCents > 0)
        {
            banco.Lancamentos.Add(new Lancamento(TipoDeLancamento.TaxaAvalia, cobranca.Id, -cobranca.TaxaAvaliaCents, $"Taxa da plataforma, placa {placa}", quando, venda.Id));
        }

        if (venda.ComissaoCents > 0)
        {
            banco.Lancamentos.Add(new Lancamento(TipoDeLancamento.ComissaoDevida, cobranca.Id, -venda.ComissaoCents, $"Comissão da placa {placa}", quando, venda.Id, venda.VendedorId));
        }
    }

    /// <summary>A placa ganha mais um ano; renovação não comissiona.</summary>
    private async Task RenovacaoPaga(Cobranca cobranca, CancellationToken ct)
    {
        var renovacao = await banco.Renovacoes.SingleOrDefaultAsync(r => r.Id == cobranca.OrigemId, ct);
        if (renovacao is null)
        {
            return;
        }

        var placa = await banco.Placas.SingleAsync(p => p.Id == renovacao.PlacaId, ct);
        var quando = cobranca.PagaEm!.Value;
        if (!renovacao.Confirmar(placa, quando) || await JaLancada(cobranca.Id, ct))
        {
            return;
        }

        banco.Lancamentos.Add(new Lancamento(TipoDeLancamento.Receita, cobranca.Id, cobranca.ValorCents, $"Renovação da placa {placa.Codigo}", quando, renovacao.VendaId));
        if (cobranca.TaxaAvaliaCents > 0)
        {
            banco.Lancamentos.Add(new Lancamento(TipoDeLancamento.TaxaAvalia, cobranca.Id, -cobranca.TaxaAvaliaCents, $"Taxa da plataforma, renovação {placa.Codigo}", quando, renovacao.VendaId));
        }
    }

    /// <summary>
    /// Estorno, e não exclusão, na competência de hoje. A comissão estornada
    /// sai do saldo do vendedor e é descontada no próximo repasse. O vencimento
    /// de uma renovação estornada não volta sozinho: o dono decide na ficha.
    /// </summary>
    private async Task Estornar(Cobranca cobranca, DateTimeOffset agora, CancellationToken ct)
    {
        var originais = await banco.Lancamentos.Where(l => l.OrigemId == cobranca.Id && l.EstornaId == null).ToListAsync(ct);
        var jaEstornadas = await banco.Lancamentos.Where(l => l.OrigemId == cobranca.Id && l.EstornaId != null).Select(l => l.EstornaId).ToListAsync(ct);
        foreach (var linha in originais.Where(l => !jaEstornadas.Contains(l.Id)))
        {
            banco.Lancamentos.Add(linha.Estorno("pagamento estornado no Pagar.me", agora));
        }
    }

    private Task<bool> JaLancada(Guid cobrancaId, CancellationToken ct) =>
        banco.Lancamentos.AnyAsync(l => l.OrigemId == cobrancaId && l.EstornaId == null, ct);
}
