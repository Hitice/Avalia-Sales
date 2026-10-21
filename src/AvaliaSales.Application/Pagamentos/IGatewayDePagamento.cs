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

public interface IGatewayDePagamento
{
    /// <summary>Recebedor da Avalia, que fica com a taxa de cada venda.</summary>
    string RecebedorDaAvalia { get; }

    Task<PedidoDoGateway> CriarPedido(PedidoNoGateway pedido, CancellationToken ct);

    /// <summary>Busca pelo nosso código. Nulo quando o pedido não chegou a ser criado.</summary>
    Task<PedidoDoGateway?> BuscarPorCodigo(string codigo, CancellationToken ct);
}
