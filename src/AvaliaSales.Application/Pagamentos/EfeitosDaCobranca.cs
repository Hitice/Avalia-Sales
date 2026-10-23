using AvaliaSales.Application.Avisos;
using AvaliaSales.Domain.Financeiro;
using AvaliaSales.Domain.Pagamentos;
using AvaliaSales.Domain.Pedidos;
using AvaliaSales.Domain.Servicos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Application.Pagamentos;

/// <summary>
/// O que muda no negócio quando a cobrança muda: pedido de placas pago, razão
/// da venda, renovação estendendo a placa, estorno desfazendo o razão. Roda
/// antes do SaveChanges de quem aplicou a cobrança: efeito e situação gravam
/// juntos, e um evento repetido não acha nada a fazer.
/// </summary>
public sealed class EfeitosDaCobranca(IBancoDeDados banco, FilaDeEmails avisos, Erp.FilaDoErp erp, Notificacoes.Notificacoes notificacoes,
    Equipe.IContas contas, IOptions<OpcoesDaPlataforma> plataforma)
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
        banco.AvisarAoVivo("pedido", pedido.RevendedorId, operadora: true);
        await erp.Enfileirar(pedido, pedido.RevendedorId, ct);
        var revendedor = await banco.Revendedores.Where(r => r.Id == pedido.RevendedorId).Select(r => r.Nome).SingleAsync(ct);
        var site = plataforma.Value;
        avisos.Enfileirar($"pedido:{pedido.Id:N}:pago", site.EmailDaOperadora, $"Pedido pago - {pedido.Quantidade} placas - {revendedor}",
            $"""
            {revendedor} pagou o pedido de {pedido.Quantidade} placas pelo Pagar.me.

            Despache um lote de {pedido.Quantidade} placas.

            Abrir pedidos: {site.UrlDoPainel.TrimEnd('/')}/operadora/pedidos
            """);
        notificacoes.ParaOperadora(pedido.RevendedorId, "Pedido pago", $"{revendedor} pagou {pedido.Quantidade} placas.", "/operadora/pedidos");
        notificacoes.ParaDono(pedido.RevendedorId, "Pedido pago", $"Recebemos o pagamento das {pedido.Quantidade} placas.", "/painel/pedidos");
    }

    /// <summary>Receita bruta, taxa da Avalia retida no split e a comissão que passa a ser devida.</summary>
    private async Task VendaPaga(Cobranca cobranca, CancellationToken ct)
    {
        var venda = await banco.Vendas.SingleOrDefaultAsync(v => v.Id == cobranca.OrigemId, ct);
        if (venda is null || await JaLancada(cobranca.Id, ct))
        {
            return;
        }

        // O que foi vendido, nas linhas do razão: a placa pelo código, o serviço pelo nome.
        var codigo = venda.PlacaId is { } placaId ? await banco.Placas.Where(p => p.Id == placaId).Select(p => p.Codigo).SingleAsync(ct) : null;
        var produto = await banco.Produtos.AsNoTracking().SingleAsync(p => p.Id == venda.ProdutoId, ct);
        var item = codigo is null ? produto.Nome : $"placa {codigo}";
        var quando = cobranca.PagaEm!.Value;
        banco.AvisarAoVivo("pagamento", venda.RevendedorId);
        banco.Lancamentos.Add(new Lancamento(TipoDeLancamento.Receita, cobranca.Id, cobranca.ValorCents, $"Venda da {item}", quando, venda.Id, venda.VendedorId));
        if (cobranca.TaxaAvaliaCents > 0)
        {
            var oQue = venda.ParteDaAvaliaCents > 0 ? "Parte da Avalia" : "Taxa da plataforma";
            var taxa = cobranca.PorFora ? $"{oQue} a acertar, {item} recebida por fora" : $"{oQue}, {item}";
            banco.Lancamentos.Add(new Lancamento(TipoDeLancamento.TaxaAvalia, cobranca.Id, -cobranca.TaxaAvaliaCents, taxa, quando, venda.Id));
        }

        if (venda.ComissaoCents > 0)
        {
            banco.Lancamentos.Add(new Lancamento(TipoDeLancamento.ComissaoDevida, cobranca.Id, -venda.ComissaoCents, $"Comissão da {item}", quando, venda.Id, venda.VendedorId));
        }

        var cliente = await banco.Clientes.AsNoTracking().Where(c => c.Id == venda.ClienteId).Select(c => c.Nome).SingleAsync(ct);
        var vendedor = (await contas.Nomes([venda.VendedorId], ct)).GetValueOrDefault(venda.VendedorId, "A equipe");
        var valor = $"R$ {venda.ValorCents / 100},{venda.ValorCents % 100:00}";
        notificacoes.ParaDono(venda.RevendedorId, "Venda paga", $"{vendedor} vendeu {produto.Nome} para {cliente}, {valor}.", "/painel/vendas");
        if (await banco.Vendedores.AnyAsync(v => v.Id == venda.VendedorId, ct))
        {
            var comissao = venda.ComissaoCents > 0 ? $" Comissão de R$ {venda.ComissaoCents / 100},{venda.ComissaoCents % 100:00}." : string.Empty;
            notificacoes.ParaVendedor(venda.RevendedorId, venda.VendedorId, "Venda paga", $"{cliente} pagou {valor}.{comissao}", "/vendedor/inicio");
        }

        // Serviço que a Avalia executa: a venda paga vira ordem para a operadora, uma por venda.
        var doCatalogo = produto.ProdutoDaPlataformaId is { } catalogoId
            ? await banco.ProdutosDaPlataforma.AsNoTracking().SingleOrDefaultAsync(p => p.Id == catalogoId, ct)
            : null;
        if (doCatalogo is { GeraServico: true } && !await banco.OrdensDeServico.AnyAsync(o => o.VendaId == venda.Id, ct))
        {
            banco.OrdensDeServico.Add(new Domain.Servicos.OrdemDeServico(venda.RevendedorId, venda.Id, doCatalogo.Id, doCatalogo.Nome, cliente, quando));
            var revendedor = await banco.Revendedores.Where(r => r.Id == venda.RevendedorId).Select(r => r.Nome).SingleAsync(ct);
            notificacoes.ParaOperadora(venda.RevendedorId, "Serviço a fazer", $"{doCatalogo.Nome} para {cliente}, vendido por {revendedor}.", "/operadora/servicos");
            notificacoes.ParaDono(venda.RevendedorId, "Serviço na fila da Avalia", $"{doCatalogo.Nome} para {cliente} entrou na fila de execução.", "/links?aba=status");
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
        banco.AvisarAoVivo("pagamento", placa.RevendedorId);
        if (cobranca.TaxaAvaliaCents > 0)
        {
            var taxa = cobranca.PorFora ? $"Taxa da plataforma a acertar, renovação {placa.Codigo} recebida por fora" : $"Taxa da plataforma, renovação {placa.Codigo}";
            banco.Lancamentos.Add(new Lancamento(TipoDeLancamento.TaxaAvalia, cobranca.Id, -cobranca.TaxaAvaliaCents, taxa, quando, renovacao.VendaId));
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
            banco.Lancamentos.Add(linha.Estorno(cobranca.PorFora ? "recebimento por fora desfeito" : "pagamento estornado no Pagar.me", agora));
        }

        // Serviço sem pagamento não se executa: a ordem ainda aberta sai cancelada; a entregue fica, e a operadora resolve com a revenda.
        if (cobranca.Origem == OrigemDaCobranca.Venda)
        {
            var abertas = await banco.OrdensDeServico
                .Where(o => o.VendaId == cobranca.OrigemId && (o.Situacao == SituacaoDaOrdem.AFazer || o.Situacao == SituacaoDaOrdem.EmAndamento))
                .ToListAsync(ct);
            foreach (var ordem in abertas)
            {
                ordem.Cancelar();
                notificacoes.ParaOperadora(ordem.RevendedorId, "Serviço cancelado", $"{ordem.Produto} para {ordem.Cliente}: o pagamento foi estornado.", "/operadora/servicos");
            }

            if (abertas.Count > 0)
            {
                banco.AvisarAoVivo("servico", cobranca.RevendedorId, operadora: true);
            }
        }

        banco.AvisarAoVivo("pagamento", cobranca.RevendedorId);
    }

    private Task<bool> JaLancada(Guid cobrancaId, CancellationToken ct) =>
        banco.Lancamentos.AnyAsync(l => l.OrigemId == cobrancaId && l.EstornaId == null, ct);
}
