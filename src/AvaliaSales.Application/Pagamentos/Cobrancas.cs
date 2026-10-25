using AvaliaSales.Domain;
using AvaliaSales.Domain.Financeiro;
using AvaliaSales.Domain.Integracoes;
using AvaliaSales.Domain.Pagamentos;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Pagamentos;

public sealed record CriarCobranca(
    OrigemDaCobranca Origem,
    Guid? OrigemId,
    MeioDePagamento Meio,
    long ValorCents,
    string Descricao,
    Pagador Pagador,
    string? TokenDoCartao = null,
    int Parcelas = 1,
    long ParteDaAvaliaCents = 0);

public sealed record CobrancaDto(
    Guid Id,
    SituacaoDaCobranca Situacao,
    long ValorCents,
    long TaxaAvaliaCents,
    string? PixCopiaECola,
    string? PixQrCodeUrl,
    DateTimeOffset? PixExpiraEm,
    string? UrlDoLink = null);

public sealed class Cobrancas(IBancoDeDados banco, IGatewayDePagamento gateway, IRevendedorAtual atual, EfeitosDaCobranca efeitos, TimeProvider relogio)
{
    public const int ParcelasMaximas = Parcelamento.Maximo;

    /// <summary>O link do pedido de placas vale três dias; depois disso, sem pagamento, a cobrança é cancelada e outra pode sair.</summary>
    public static readonly TimeSpan ValidadeDoLink = TimeSpan.FromDays(3);

    /// <summary>Todo o valor do pedido de placas é da Avalia: 100% em pontos base.</summary>
    private const int TudoParaAvalia = 10_000;

    /// <summary>
    /// Grava a cobrança e o vínculo antes de chamar o Pagar.me. Se a chamada
    /// cair, a cobrança fica pendente sem pedido e a rotina do Worker acha o
    /// pedido pelo código, em vez de criar outro.
    /// </summary>
    public async Task<CobrancaDto> Criar(CriarCobranca comando, CancellationToken ct)
    {
        var revendedorId = atual.RevendedorId
            ?? throw new InvalidOperationException("Cobrança sem revendedor na requisição.");
        var revendedor = await banco.Revendedores.SingleAsync(r => r.Id == revendedorId, ct);

        if (revendedor.RecebedorPagarme is null)
        {
            throw new RegraDeNegocioException("O revendedor ainda não tem conta de recebimento no Pagar.me.");
        }

        if (comando.Meio is MeioDePagamento.Dinheiro or MeioDePagamento.OutroPix)
        {
            throw new RegraDeNegocioException("Escolha Pix ou cartão.");
        }

        if (comando.Meio == MeioDePagamento.Cartao && string.IsNullOrWhiteSpace(comando.TokenDoCartao))
        {
            throw new RegraDeNegocioException("Falta o cartão para cobrar.");
        }

        if (comando.Parcelas is < 1 or > ParcelasMaximas)
        {
            throw new RegraDeNegocioException($"O parcelamento vai de 1 a {ParcelasMaximas} vezes.");
        }

        // Parcelado, o cliente paga o custo do cartão; no Pix e à vista, a Avalia paga a fatia dela no custo.
        var total = Parcelamento.Total(comando.ValorCents, comando.Meio, comando.Parcelas);
        var parte = Parcelamento.ParteLiquida(comando.ParteDaAvaliaCents, comando.Meio, comando.Parcelas);
        var cobranca = new Cobranca(comando.Origem, comando.OrigemId, comando.Meio, total, revendedor.TaxaAvaliaBps, parte);
        await using (var transacao = await banco.Database.BeginTransactionAsync(ct))
        {
            // Dois toques ou duas abas cobrando a mesma venda ao mesmo tempo: a trava
            // da origem enfileira os dois, e o segundo encontra a cobrança do primeiro.
            if (comando.OrigemId is { } origem)
            {
                await banco.TravarOrigemDaCobranca(origem, ct);
                if (await banco.Cobrancas.AnyAsync(c => c.Origem == comando.Origem && c.OrigemId == origem
                        && (c.Situacao == SituacaoDaCobranca.Pendente || c.Situacao == SituacaoDaCobranca.Paga), ct))
                {
                    throw new RegraDeNegocioException("Já existe cobrança em aberto ou paga para esta venda.");
                }
            }

            banco.Cobrancas.Add(cobranca);
            banco.Vinculos.Add(new VinculoExterno(VinculoExterno.Pagarme, cobranca.Codigo, revendedorId));
            await banco.SaveChangesAsync(ct);
            await transacao.CommitAsync(ct);
        }

        var divisao = cobranca.Divisao;

        // A revenda que recebe na conta da própria Avalia tem uma regra só: o Pagar.me não aceita o mesmo recebedor duas vezes no split.
        var mesmoRecebedor = revendedor.RecebedorPagarme == gateway.RecebedorDaAvalia;
        var regras = new List<RegraDeDivisao>
        {
            new(revendedor.RecebedorPagarme, mesmoRecebedor ? cobranca.ValorCents : divisao.RevendedorCents, AssumeTaxasERisco: true),
        };
        if (divisao.AvaliaCents > 0 && !mesmoRecebedor)
        {
            regras.Add(new RegraDeDivisao(gateway.RecebedorDaAvalia, divisao.AvaliaCents, AssumeTaxasERisco: false));
        }

        var pedido = await gateway.CriarPedido(
            new PedidoNoGateway(cobranca.Codigo, comando.Descricao, cobranca.ValorCents, comando.Meio, comando.Pagador, regras, comando.TokenDoCartao, comando.Parcelas),
            ct);
        await Aplicar(cobranca, pedido, ct);
        await banco.SaveChangesAsync(ct);
        return ParaDto(cobranca);
    }

    /// <summary>
    /// Cobrança pelo checkout do Pagar.me, sem split: o pedido de placas é
    /// pago à Avalia. O pedido no Pagar.me só nasce quando o revendedor paga;
    /// até lá a conferência procura pelo código.
    /// </summary>
    public async Task<CobrancaDto> CriarLink(OrigemDaCobranca origem, Guid origemId, long valorCents, string descricao, CancellationToken ct)
    {
        var revendedorId = atual.RevendedorId ?? throw new InvalidOperationException("Cobrança sem revendedor na requisição.");

        // Dois toques em Pagar: o segundo espera o primeiro terminar o link e recebe o mesmo, em vez de gerar outro link vivo.
        await using var trava = await banco.SegurarOrigemDaCobranca(origemId, ct);
        var aberta = await banco.Cobrancas.AsNoTracking()
            .Where(c => c.Origem == origem && c.OrigemId == origemId && (c.Situacao == SituacaoDaCobranca.Pendente || c.Situacao == SituacaoDaCobranca.Paga))
            .OrderByDescending(c => c.CriadaEm).FirstOrDefaultAsync(ct);
        if (aberta is not null)
        {
            return aberta.UrlDoLink is not null && aberta.Situacao == SituacaoDaCobranca.Pendente
                ? ParaDto(aberta)
                : throw new RegraDeNegocioException("Já existe cobrança em aberto ou paga para este pedido.");
        }

        var cobranca = new Cobranca(origem, origemId, MeioDePagamento.Link, valorCents, TudoParaAvalia);
        banco.Cobrancas.Add(cobranca);
        banco.Vinculos.Add(new VinculoExterno(VinculoExterno.Pagarme, cobranca.Codigo, revendedorId));
        await banco.SaveChangesAsync(ct);

        try
        {
            var link = await gateway.CriarLink(new LinkNoGateway(cobranca.Codigo, descricao, valorCents, (int)ValidadeDoLink.TotalMinutes), ct);
            cobranca.RegistrarLink(link.Id, link.Url);
        }
        catch (IntegracaoException)
        {
            // Link não criado não cobra ninguém: a cobrança falha na hora e o revendedor pede de novo.
            cobranca.Atualizar(SituacaoDaCobranca.Falhou, null);
            await banco.SaveChangesAsync(ct);
            throw;
        }

        await banco.SaveChangesAsync(ct);
        return ParaDto(cobranca);
    }

    /// <summary>
    /// Para a cobrança que ficou sem pedido: acha pelo código e liga; se depois
    /// do prazo o pedido não existir, a cobrança falhou e pode ser refeita.
    /// </summary>
    public async Task<bool> Retomar(Guid cobrancaId, DateTimeOffset desistirSeCriadaAntesDe, CancellationToken ct)
    {
        var cobranca = await banco.Cobrancas.SingleAsync(c => c.Id == cobrancaId, ct);
        if (cobranca.PedidoNoGateway is not null || cobranca.Situacao != SituacaoDaCobranca.Pendente)
        {
            return false;
        }

        var pedido = await gateway.BuscarPorCodigo(cobranca.Codigo, ct);
        var mudou = pedido is not null
            ? await Aplicar(cobranca, pedido, ct)
            : cobranca.CriadaEm < desistirSeCriadaAntesDe && cobranca.Atualizar(SituacaoDaCobranca.Falhou, null);

        await banco.SaveChangesAsync(ct);
        return mudou;
    }

    /// <summary>
    /// Rede de segurança do webhook perdido: reconsulta a cobrança pendente e
    /// aplica o que o Pagar.me disser. Link vencido sem pagamento vira
    /// cancelado. Devolve se mudou.
    /// </summary>
    public async Task<bool> Conferir(Guid cobrancaId, DateTimeOffset agora, CancellationToken ct)
    {
        var cobranca = await banco.Cobrancas.SingleAsync(c => c.Id == cobrancaId, ct);
        var pedido = await gateway.BuscarPorCodigo(cobranca.Codigo, ct);
        var mudou = pedido is not null
            ? await Aplicar(cobranca, pedido, ct)
            : cobranca.LinkNoGateway is not null && cobranca.CriadaEm + ValidadeDoLink < agora && cobranca.Atualizar(SituacaoDaCobranca.Cancelada, null);
        cobranca.MarcarConferida(agora);
        await banco.SaveChangesAsync(ct);
        return mudou;
    }

    /// <summary>
    /// Liga o pedido à cobrança, aplica a situação lida no Pagar.me e, se ela
    /// mudou, o efeito no negócio (pedido pago, razão, renovação), tudo antes
    /// do mesmo SaveChanges de quem chamou.
    /// </summary>
    public async Task<bool> Aplicar(Cobranca cobranca, PedidoDoGateway pedido, CancellationToken ct)
    {
        var ligou = cobranca.PedidoNoGateway is null;
        cobranca.RegistrarPedido(pedido.Id, pedido.PixCopiaECola, pedido.PixQrCodeUrl, pedido.PixExpiraEm);
        var mudou = cobranca.Atualizar(pedido.Situacao, pedido.PagoEm);
        if (mudou)
        {
            await efeitos.Aplicar(cobranca, relogio.GetUtcNow(), ct);
        }

        return mudou || ligou;
    }

    /// <summary>
    /// O vendedor declara que recebeu fora do Pagar.me. A cobrança nasce paga e
    /// o efeito no negócio é o mesmo do Pix que caiu, no mesmo SaveChanges.
    /// </summary>
    public async Task<Cobranca> RegistrarPorFora(OrigemDaCobranca origem, Guid origemId, MeioDePagamento meio, long valorCents, CancellationToken ct, long parteDaAvaliaCents = 0)
    {
        var revendedorId = atual.RevendedorId ?? throw new InvalidOperationException("Cobrança sem revendedor na requisição.");
        var taxa = await banco.Revendedores.Where(r => r.Id == revendedorId).Select(r => r.TaxaAvaliaBps).SingleAsync(ct);
        var cobranca = Cobranca.RecebidaPorFora(origem, origemId, meio, valorCents, taxa, relogio.GetUtcNow(), parteDaAvaliaCents);
        banco.Cobrancas.Add(cobranca);
        await efeitos.Aplicar(cobranca, relogio.GetUtcNow(), ct);
        return cobranca;
    }

    /// <summary>Desfaz a confirmação por fora com estorno no razão, como um estorno do Pagar.me.</summary>
    public async Task DesfazerPorFora(Cobranca cobranca, CancellationToken ct)
    {
        cobranca.DesfazerRecebimento();
        await efeitos.Aplicar(cobranca, relogio.GetUtcNow(), ct);
    }

    /// <summary>
    /// A cobrança da venda desfeita: a do Pagar.me é cancelada lá (a pendente não
    /// pode mais ser paga, a paga volta inteira ao cliente) e a recebida por fora
    /// é desfeita aqui. O estorno vai ao razão já, sem esperar o webhook, que
    /// depois chega e não muda mais nada.
    /// </summary>
    public async Task Cancelar(Cobranca cobranca, CancellationToken ct)
    {
        if (cobranca.PorFora)
        {
            if (cobranca.Situacao == SituacaoDaCobranca.Paga)
            {
                await DesfazerPorFora(cobranca, ct);
            }

            return;
        }

        // Pedido que caiu no meio da criação ainda pode existir lá, pelo código: cancela também.
        var pedido = cobranca.PedidoNoGateway ?? (await gateway.BuscarPorCodigo(cobranca.Codigo, ct))?.Id;
        if (pedido is not null)
        {
            try
            {
                await gateway.CancelarPedido(pedido, ct);
            }
            catch (IntegracaoException recusa) when (recusa.Status is >= 400 and < 500)
            {
                throw Recusa(recusa);
            }
        }

        var lida = cobranca.Situacao == SituacaoDaCobranca.Paga ? SituacaoDaCobranca.Estornada : SituacaoDaCobranca.Cancelada;
        if (cobranca.Atualizar(lida, null))
        {
            await efeitos.Aplicar(cobranca, relogio.GetUtcNow(), ct);
        }
    }

    /// <summary>
    /// Desfaz uma venda do grupo pago, com o resto de pé: devolve ao cliente o
    /// que ela recebeu (a receita dela no razão), pelo Pagar.me em estorno
    /// parcial ou pela revenda quando o pagamento foi por fora, e estorna as
    /// linhas dela. Devolvido tudo, a cobrança passa a estornada.
    /// </summary>
    public async Task EstornarVenda(Cobranca cobranca, Guid vendaId, CancellationToken ct)
    {
        var estornadas = banco.Lancamentos.Where(l => l.EstornaId != null).Select(l => l.EstornaId);
        var linhas = await banco.Lancamentos.Where(l => l.OrigemId == cobranca.Id && l.VendaId == vendaId && l.EstornaId == null && !estornadas.Contains(l.Id))
            .ToListAsync(ct);
        var devolver = linhas.Where(l => l.Tipo == TipoDeLancamento.Receita).Sum(l => l.ValorCents);
        var agora = relogio.GetUtcNow();
        if (devolver > 0)
        {
            if (!cobranca.PorFora)
            {
                var pedido = cobranca.PedidoNoGateway ?? throw new RegraDeNegocioException("Esta cobrança não tem pedido no Pagar.me. Confira no painel do Pagar.me.");

                // A regra confere antes de o dinheiro sair: o que passa do pago não chega ao Pagar.me.
                cobranca.ConferirEstorno(devolver);
                try
                {
                    // Repetir depois de resposta perdida não devolve duas vezes: o Pagar.me já tem este tanto devolvido.
                    await gateway.EstornarParte(pedido, devolver, cobranca.EstornadoCents, ct);
                }
                catch (IntegracaoException recusa) when (recusa.Status is >= 400 and < 500)
                {
                    throw Recusa(recusa);
                }
            }

            // Devolvido tudo, o estorno da cobrança inteira estorna o que sobrou, esta venda junto; senão, só as linhas dela.
            if (cobranca.EstornarParte(devolver) && cobranca.Atualizar(SituacaoDaCobranca.Estornada, null))
            {
                await efeitos.Aplicar(cobranca, agora, ct);
            }
            else
            {
                foreach (var linha in linhas)
                {
                    banco.Lancamentos.Add(linha.Estorno(cobranca.PorFora ? "venda do grupo desfeita, devolvida por fora" : "venda do grupo estornada no Pagar.me", agora));
                }
            }
        }

        await efeitos.EncerrarVenda(vendaId, ct);
    }

    /// <summary>Recusa do Pagar.me é resposta para quem cancela, não falha do sistema: nada foi gravado, a venda segue paga.</summary>
    private static RegraDeNegocioException Recusa(IntegracaoException recusa) =>
        new(recusa.Message.Contains("Saldo insuficiente", StringComparison.OrdinalIgnoreCase)
            ? "O Pagar.me não estornou por falta de saldo. Estorne pelo painel do Pagar.me, que devolve a taxa junto, e cancele a venda aqui de novo."
            : "O Pagar.me recusou o cancelamento desta cobrança. Confira no painel do Pagar.me.");

    public static CobrancaDto ParaDto(Cobranca c) =>
        new(c.Id, c.Situacao, c.ValorCents, c.TaxaAvaliaCents, c.PixCopiaECola, c.PixQrCodeUrl, c.PixExpiraEm, c.UrlDoLink);
}
