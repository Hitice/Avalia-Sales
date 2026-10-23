using AvaliaSales.Domain.Pagamentos;

namespace AvaliaSales.Application.Pagamentos;

public sealed record Pagador(string Nome, string Email, string Documento, string? Celular);

/// <summary>Uma regra do split. Quem assume taxa, risco e resto do arredondamento é o revendedor.</summary>
public sealed record RegraDeDivisao(string Recebedor, long ValorCents, bool AssumeTaxasERisco);

public sealed record PedidoNoGateway(
    string Codigo,
    string Descricao,
    long ValorCents,
    MeioDePagamento Meio,
    Pagador Pagador,
    IReadOnlyList<RegraDeDivisao> Divisao,
    string? TokenDoCartao,
    int Parcelas);

public sealed record PedidoDoGateway(
    string Id,
    string Codigo,
    SituacaoDaCobranca Situacao,
    DateTimeOffset? PagoEm,
    string? PixCopiaECola,
    string? PixQrCodeUrl,
    DateTimeOffset? PixExpiraEm);

/// <summary>
/// Link de pagamento (checkout do Pagar.me) para o pedido de placas: o
/// revendedor paga na página do Pagar.me, sem split, tudo para a Avalia.
/// O código vai como order_code, para o pedido pago ser achado por ele.
/// </summary>
public sealed record LinkNoGateway(string Codigo, string Descricao, long ValorCents, int ValidadeEmMinutos);

public sealed record LinkDoGateway(string Id, string Url);

/// <summary>Recebedor como o Pagar.me o vê: nome e situação (active quando pode receber).</summary>
public sealed record RecebedorDoGateway(string Id, string Nome, string Situacao);

public interface IGatewayDePagamento
{
    /// <summary>Recebedor da Avalia, que fica com a taxa de cada venda.</summary>
    string RecebedorDaAvalia { get; }

    Task<PedidoDoGateway> CriarPedido(PedidoNoGateway pedido, CancellationToken ct);

    Task<LinkDoGateway> CriarLink(LinkNoGateway link, CancellationToken ct);

    /// <summary>Busca pelo nosso código. Nulo quando o pedido não chegou a ser criado.</summary>
    Task<PedidoDoGateway?> BuscarPorCodigo(string codigo, CancellationToken ct);

    /// <summary>Lê um recebedor; confere a chave e o id antes da primeira cobrança.</summary>
    Task<RecebedorDoGateway> BuscarRecebedor(string id, CancellationToken ct);
}
