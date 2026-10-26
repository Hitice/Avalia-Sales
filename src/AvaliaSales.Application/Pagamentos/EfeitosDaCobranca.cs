using AvaliaSales.Application.Avisos;
using AvaliaSales.Domain.Financeiro;
using AvaliaSales.Domain.Pagamentos;
using AvaliaSales.Domain.Pedidos;
using AvaliaSales.Domain.Servicos;
using AvaliaSales.Domain.Vendas;
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
    Equipe.IContas contas, IOptions<OpcoesDaPlataforma> plataforma, Financeiro.LivroDaOperadora livro)
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

    /// <summary>
    /// Receita bruta, taxa da Avalia retida no split e a comissão que passa a
    /// ser devida, de cada venda que a cobrança paga: a sozinha, ou as do grupo
    /// (as que seguem de pé). No grupo, cada venda recebe o valor dela, e a
    /// última fica com o que sobra (o custo do parcelamento e os centavos do
    /// rateio da taxa), para a soma bater com a cobrança.
    /// </summary>
    private async Task VendaPaga(Cobranca cobranca, CancellationToken ct)
    {
        var origem = cobranca.OrigemId;
        var vendas = (await banco.Vendas.Where(v => v.Id == origem || v.GrupoId == origem).ToListAsync(ct))
            .Where(v => v.GrupoId is null || v.Situacao == SituacaoDaVenda.Registrada)
            .OrderBy(v => v.VendidaEm).ThenBy(v => v.Id).ToList();
        var soma = vendas.Sum(v => v.ValorCents);
        var (receitaQueSobra, taxaQueSobra) = (cobranca.ValorCents, cobranca.TaxaAvaliaCents);
        for (var i = 0; i < vendas.Count; i++)
        {
            var venda = vendas[i];
            var ultima = i == vendas.Count - 1;
            var receita = ultima ? receitaQueSobra : venda.ValorCents;
            var taxa = ultima || soma == 0 ? taxaQueSobra : cobranca.TaxaAvaliaCents * venda.ValorCents / soma;
            (receitaQueSobra, taxaQueSobra) = (receitaQueSobra - receita, taxaQueSobra - taxa);
            await LancarVenda(cobranca, venda, receita, taxa, ct);
        }
    }

    private async Task LancarVenda(Cobranca cobranca, Domain.Vendas.Venda venda, long receitaCents, long taxaCents, CancellationToken ct)
    {
        if (await banco.Lancamentos.AnyAsync(l => l.OrigemId == cobranca.Id && l.VendaId == venda.Id && l.EstornaId == null, ct))
        {
            return;
        }

        // O que foi vendido, nas linhas do razão: a placa pelo código, o serviço pelo nome.
        var codigo = venda.PlacaId is { } placaId ? await banco.Placas.Where(p => p.Id == placaId).Select(p => p.Codigo).SingleAsync(ct) : null;
        var produto = await banco.Produtos.AsNoTracking().SingleAsync(p => p.Id == venda.ProdutoId, ct);
        var nome = venda.Quantidade > 1 ? $"{venda.Quantidade}x {produto.Nome}" : produto.Nome;
        var item = codigo is null ? nome : $"placa {codigo}";
        var quando = cobranca.PagaEm!.Value;
        banco.AvisarAoVivo("pagamento", venda.RevendedorId);
        banco.Lancamentos.Add(new Lancamento(TipoDeLancamento.Receita, cobranca.Id, receitaCents, $"Venda da {item}", quando, venda.Id, venda.VendedorId));
        if (taxaCents > 0)
        {
            var oQue = venda.ParteDaAvaliaCents > 0 ? Lancamento.InicioDaParteDaAvalia : "Taxa da plataforma";
            var taxa = cobranca.PorFora ? $"{oQue} a acertar, {item} recebida por fora" : $"{oQue}, {item}";
            var linhaDaTaxa = new Lancamento(TipoDeLancamento.TaxaAvalia, cobranca.Id, -taxaCents, taxa, quando, venda.Id);
            banco.Lancamentos.Add(linhaDaTaxa);
            livro.Taxa(linhaDaTaxa, venda.RevendedorId, cobranca.PorFora);
        }

        if (venda.ComissaoCents > 0)
        {
            banco.Lancamentos.Add(new Lancamento(TipoDeLancamento.ComissaoDevida, cobranca.Id, -venda.ComissaoCents, $"Comissão da {item}", quando, venda.Id, venda.VendedorId));
        }

        // Teste gratuito que virou venda: os 12 meses contam do pagamento, e a placa que saiu do ar volta.
        if (venda.EmTeste && venda.PlacaId is { } daVenda)
        {
            var voltar = venda.ConverterTeste(quando);
            var placa = await banco.Placas.SingleAsync(p => p.Id == daVenda, ct);
            placa.IniciarVigencia(quando, voltar);
        }

        var cliente = await banco.Clientes.AsNoTracking().Where(c => c.Id == venda.ClienteId).Select(c => c.Nome).SingleAsync(ct);
        var vendedor = (await contas.Nomes([venda.VendedorId], ct)).GetValueOrDefault(venda.VendedorId, "A equipe");
        var valor = $"R$ {venda.ValorCents / 100},{venda.ValorCents % 100:00}";
        notificacoes.ParaDono(venda.RevendedorId, "Venda paga", $"{vendedor} vendeu {nome} para {cliente}, {valor}.", "/painel/vendas", venda.Id);
        if (await banco.Vendedores.AnyAsync(v => v.Id == venda.VendedorId, ct))
        {
            var comissao = venda.ComissaoCents > 0 ? $" Comissão de R$ {venda.ComissaoCents / 100},{venda.ComissaoCents % 100:00}." : string.Empty;
            notificacoes.ParaVendedor(venda.RevendedorId, venda.VendedorId, "Venda paga", $"{cliente} pagou {valor}.{comissao}", "/vendedor/inicio", venda.Id);
        }

        // Serviço que a Avalia executa: a venda paga vira ordem para a operadora, uma por venda, com a quantidade dela.
        var doCatalogo = produto.ProdutoDaPlataformaId is { } catalogoId
            ? await banco.ProdutosDaPlataforma.AsNoTracking().SingleOrDefaultAsync(p => p.Id == catalogoId, ct)
            : null;
        if (doCatalogo is { GeraServico: true } && !await banco.OrdensDeServico.AnyAsync(o => o.VendaId == venda.Id, ct))
        {
            banco.OrdensDeServico.Add(new Domain.Servicos.OrdemDeServico(venda.RevendedorId, venda.Id, doCatalogo.Id, doCatalogo.Nome, cliente, quando, venda.Quantidade));
            var revendedor = await banco.Revendedores.Where(r => r.Id == venda.RevendedorId).Select(r => r.Nome).SingleAsync(ct);
            var servico = venda.Quantidade > 1 ? $"{venda.Quantidade}x {doCatalogo.Nome}" : doCatalogo.Nome;
            notificacoes.ParaOperadora(venda.RevendedorId, "Serviço a fazer", $"{servico} para {cliente}, vendido por {revendedor}.", "/operadora/servicos", venda.Id);
            notificacoes.ParaDono(venda.RevendedorId, "Serviço na fila da Avalia", $"{servico} para {cliente} entrou na fila de execução.", "/links?aba=status", venda.Id);
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
            var linhaDaTaxa = new Lancamento(TipoDeLancamento.TaxaAvalia, cobranca.Id, -cobranca.TaxaAvaliaCents, taxa, quando, renovacao.VendaId);
            banco.Lancamentos.Add(linhaDaTaxa);
            livro.Taxa(linhaDaTaxa, placa.RevendedorId, cobranca.PorFora);
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
            var estorno = linha.Estorno(cobranca.PorFora ? "recebimento por fora desfeito" : "pagamento estornado no Pagar.me", agora);
            banco.Lancamentos.Add(estorno);
            livro.TaxaEstornada(linha, estorno, cobranca.RevendedorId, cobranca.PorFora);
        }

        if (cobranca.Origem == OrigemDaCobranca.Venda && cobranca.OrigemId is { } origem)
        {
            // A situação vem da memória: o cancelamento daqui já marcou a venda antes de estornar.
            var vendas = await banco.Vendas.Where(v => v.Id == origem || v.GrupoId == origem).ToListAsync(ct);
            foreach (var venda in vendas)
            {
                await EncerrarVenda(venda.Id, ct);
            }

            // Estorno que não saiu do cancelamento daqui (painel do Pagar.me, contestação): a venda segue de pé até o dono decidir o destino da placa.
            var dePe = vendas.Where(v => v.Situacao == SituacaoDaVenda.Registrada).ToList();
            if (!cobranca.PorFora && dePe.Count > 0)
            {
                var cliente = await banco.Clientes.AsNoTracking().Where(c => c.Id == dePe[0].ClienteId).Select(c => c.Nome).SingleAsync(ct);
                var proximo = dePe.Count > 1 ? "Cancele as vendas aqui também."
                    : dePe[0].PlacaId is null ? "Cancele a venda aqui também." : "Cancele a venda para tirar a placa do ar.";
                notificacoes.ParaDono(dePe[0].RevendedorId, "Pagamento estornado", $"{cliente}: o pagamento foi estornado no Pagar.me. {proximo}", "/painel/vendas");
            }
        }

        banco.AvisarAoVivo("pagamento", cobranca.RevendedorId);
    }

    /// <summary>
    /// A venda sem pagamento: o que ela abriu no sino se encerra; a ordem de
    /// serviço aberta sai cancelada, e a já entregue fica, com o aviso para a
    /// operadora tirar do ar o que foi entregue ou acertar com a revenda.
    /// </summary>
    public async Task EncerrarVenda(Guid vendaId, CancellationToken ct)
    {
        await notificacoes.EncerrarDaVenda(vendaId, ct);
        var ordens = await banco.OrdensDeServico.Where(o => o.VendaId == vendaId && o.Situacao != SituacaoDaOrdem.Cancelada).ToListAsync(ct);
        foreach (var ordem in ordens)
        {
            if (ordem.Situacao == SituacaoDaOrdem.Entregue)
            {
                notificacoes.ParaOperadora(ordem.RevendedorId, "Serviço entregue sem pagamento",
                    $"{ordem.Produto} para {ordem.Cliente}: o pagamento foi estornado depois da entrega. Tire do ar o que foi entregue ou acerte com a revenda.", "/operadora/servicos");
                continue;
            }

            ordem.Cancelar();
            notificacoes.ParaOperadora(ordem.RevendedorId, "Serviço cancelado", $"{ordem.Produto} para {ordem.Cliente}: o pagamento foi estornado.", "/operadora/servicos");
        }

        if (ordens.Count > 0)
        {
            banco.AvisarAoVivo("servico", ordens[0].RevendedorId, operadora: true);
        }
    }

    private Task<bool> JaLancada(Guid cobrancaId, CancellationToken ct) =>
        banco.Lancamentos.AnyAsync(l => l.OrigemId == cobrancaId && l.EstornaId == null, ct);
}
