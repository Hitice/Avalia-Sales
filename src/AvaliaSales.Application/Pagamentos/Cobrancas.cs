using AvaliaSales.Domain;
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
    int Parcelas = 1);

public sealed record CobrancaDto(
    Guid Id,
    SituacaoDaCobranca Situacao,
    long ValorCents,
    long TaxaAvaliaCents,
    string? PixCopiaECola,
    string? PixQrCodeUrl,
    DateTimeOffset? PixExpiraEm);

public sealed class Cobrancas(IBancoDeDados banco, IGatewayDePagamento gateway, IRevendedorAtual atual)
{
    public const int ParcelasMaximas = 12;

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

        if (comando.Meio == MeioDePagamento.Cartao && string.IsNullOrWhiteSpace(comando.TokenDoCartao))
        {
            throw new RegraDeNegocioException("Falta o cartão para cobrar.");
        }

        if (comando.Parcelas is < 1 or > ParcelasMaximas)
        {
            throw new RegraDeNegocioException($"O parcelamento vai de 1 a {ParcelasMaximas} vezes.");
        }

        var cobranca = new Cobranca(comando.Origem, comando.OrigemId, comando.Meio, comando.ValorCents, revendedor.TaxaAvaliaBps);
        banco.Cobrancas.Add(cobranca);
        banco.Vinculos.Add(new VinculoExterno(VinculoExterno.Pagarme, cobranca.Codigo, revendedorId));
        await banco.SaveChangesAsync(ct);

        var divisao = cobranca.Divisao;
        var regras = new List<RegraDeDivisao> { new(revendedor.RecebedorPagarme, divisao.RevendedorCents, AssumeTaxasERisco: true) };
        if (divisao.AvaliaCents > 0)
        {
            regras.Add(new RegraDeDivisao(gateway.RecebedorDaAvalia, divisao.AvaliaCents, AssumeTaxasERisco: false));
        }

        var pedido = await gateway.CriarPedido(
            new PedidoNoGateway(cobranca.Codigo, comando.Descricao, cobranca.ValorCents, comando.Meio, comando.Pagador, regras, comando.TokenDoCartao, comando.Parcelas),
            ct);
        AplicarPedido(cobranca, pedido);
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
            ? AplicarPedido(cobranca, pedido)
            : cobranca.CriadaEm < desistirSeCriadaAntesDe && cobranca.Atualizar(SituacaoDaCobranca.Falhou, null);

        await banco.SaveChangesAsync(ct);
        return mudou;
    }

    /// <summary>Liga o pedido à cobrança e aplica a situação lida no Pagar.me.</summary>
    public static bool AplicarPedido(Cobranca cobranca, PedidoDoGateway pedido)
    {
        var ligou = cobranca.PedidoNoGateway is null;
        cobranca.RegistrarPedido(pedido.Id, pedido.PixCopiaECola, pedido.PixQrCodeUrl, pedido.PixExpiraEm);
        return cobranca.Atualizar(pedido.Situacao, pedido.PagoEm) || ligou;
    }

    private static CobrancaDto ParaDto(Cobranca c) =>
        new(c.Id, c.Situacao, c.ValorCents, c.TaxaAvaliaCents, c.PixCopiaECola, c.PixQrCodeUrl, c.PixExpiraEm);
}
