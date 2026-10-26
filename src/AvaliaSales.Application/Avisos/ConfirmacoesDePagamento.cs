using AvaliaSales.Domain.Pagamentos;
using AvaliaSales.Domain.Pedidos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Application.Avisos;

/// <summary>
/// O e-mail ao dono da revenda quando um pagamento entra: pedido de placas pago (pelo Pagar.me ou registrado pela operadora)
/// e venda paga. Vai para a fila na transação do pagamento; a chave impede o mesmo aviso duas vezes se o evento repetir.
/// </summary>
public sealed class ConfirmacoesDePagamento(IBancoDeDados banco, FilaDeEmails emails, Equipe.IContas contas, IOptions<OpcoesDaPlataforma> plataforma)
{
    public async Task PedidoPago(PedidoDePlacas pedido, CancellationToken ct)
    {
        var painel = plataforma.Value.UrlDoPainel.TrimEnd('/');
        foreach (var dono in await contas.EmailsDosDonos(pedido.RevendedorId, ct))
        {
            emails.Enfileirar($"pagamento:pedido:{pedido.Id:N}:{dono}", dono, $"Pagamento confirmado - pedido de {pedido.Quantidade} placas",
                $"""
                Recebemos o pagamento do seu pedido de {pedido.Quantidade} placas, {Financeiro.RecebimentosPorFora.Reais(pedido.ValorCents)}.

                A Avalia vai produzir e despachar o lote. Você recebe outro aviso com o número dele para ativar.

                Acompanhar o pedido: {painel}/painel/pedidos
                """);
        }
    }

    /// <summary>Uma mensagem por cobrança: a venda sozinha ou todas as do grupo que ela pagou.</summary>
    public async Task VendaPaga(Cobranca cobranca, IReadOnlyList<Domain.Vendas.Venda> vendas, CancellationToken ct)
    {
        if (vendas.Count == 0)
        {
            return;
        }

        var revendedorId = vendas[0].RevendedorId;
        var donos = await contas.EmailsDosDonos(revendedorId, ct);
        if (donos.Count == 0)
        {
            return;
        }

        var cliente = await banco.Clientes.AsNoTracking().Where(c => c.Id == vendas[0].ClienteId).Select(c => c.Nome).SingleAsync(ct);
        var linhas = new List<string>();
        foreach (var venda in vendas)
        {
            var produto = await banco.Produtos.AsNoTracking().Where(p => p.Id == venda.ProdutoId).Select(p => p.Nome).SingleAsync(ct);
            var codigo = venda.PlacaId is { } placaId ? await banco.Placas.AsNoTracking().Where(p => p.Id == placaId).Select(p => p.Codigo).SingleAsync(ct) : null;
            var nome = codigo is null ? (venda.Quantidade > 1 ? $"{venda.Quantidade}x {produto}" : produto) : $"Placa {codigo}";
            linhas.Add($"- {nome}: {Financeiro.RecebimentosPorFora.Reais(venda.ValorCents)}");
        }

        var forma = cobranca.Meio switch
        {
            MeioDePagamento.Pix => "Pix",
            MeioDePagamento.Cartao => "cartão",
            MeioDePagamento.Link => "checkout do Pagar.me",
            MeioDePagamento.Dinheiro => "dinheiro, registrado como recebido por fora",
            MeioDePagamento.OutroPix => "outro Pix, registrado como recebido por fora",
            _ => "pagamento",
        };
        var painel = plataforma.Value.UrlDoPainel.TrimEnd('/');
        var itens = string.Join('\n', linhas);
        foreach (var dono in donos)
        {
            emails.Enfileirar($"pagamento:venda:{cobranca.Id:N}:{dono}", dono, $"Pagamento confirmado - {cliente}, {Financeiro.RecebimentosPorFora.Reais(cobranca.ValorCents)}",
                $"""
                {cliente} pagou por {forma}.

                {itens}

                Total cobrado: {Financeiro.RecebimentosPorFora.Reais(cobranca.ValorCents)}

                Ver as vendas: {painel}/painel/vendas
                """);
        }
    }
}
