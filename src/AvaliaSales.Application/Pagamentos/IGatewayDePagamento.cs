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
    /// <summary>Recebedor da Avalia, que fica com a parte dela nos serviços e com o pedido de placas.</summary>
    string RecebedorDaAvalia { get; }

    Task<PedidoDoGateway> CriarPedido(PedidoNoGateway pedido, CancellationToken ct);

    Task<LinkDoGateway> CriarLink(LinkNoGateway link, CancellationToken ct);

    /// <summary>Busca pelo nosso código. Nulo quando o pedido não chegou a ser criado.</summary>
    Task<PedidoDoGateway?> BuscarPorCodigo(string codigo, CancellationToken ct);

    /// <summary>Lê um recebedor; confere a chave e o id antes da primeira cobrança.</summary>
    Task<RecebedorDoGateway> BuscarRecebedor(string id, CancellationToken ct);

    /// <summary>
    /// Cancela as cobranças do pedido: a pendente deixa de poder ser paga, e a
    /// paga é estornada inteira ao cliente, pelo split de quando foi paga.
    /// </summary>
    Task CancelarPedido(string pedido, CancellationToken ct);

    /// <summary>Cancela o link de pagamento ainda não pago: o pedido no Pagar.me só nasce com o pagamento, então é o link que deixa de valer.</summary>
    Task CancelarLink(string link, CancellationToken ct);

    /// <summary>
    /// Devolve só uma parte do pedido pago: a venda do grupo desfeita sozinha.
    /// O resto segue pago; devolvido tudo, a cobrança aparece estornada.
    /// O Pagar.me não tem chave de idempotência: <paramref name="jaEstornadoCents"/> é o
    /// que o sistema registra como devolvido antes desta chamada, e se o pedido já
    /// tem devolvido jaEstornado + valor (a chamada anterior valeu e a resposta caiu),
    /// nada é devolvido de novo.
    /// </summary>
    Task EstornarParte(string pedido, long valorCents, long jaEstornadoCents, CancellationToken ct);
}
