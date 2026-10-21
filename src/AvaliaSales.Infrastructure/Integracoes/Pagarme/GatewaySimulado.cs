using System.Globalization;
using AvaliaSales.Application.Pagamentos;
using AvaliaSales.Domain.Pagamentos;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Infrastructure.Integracoes.Pagarme;

/// <summary>
/// Pagar.me de mentira para rodar a plataforma na máquina sem conta no
/// sandbox. Sem estado: o código do pedido é o id da cobrança, um UUID v7, que
/// carrega a hora da criação. A API e o Worker, processos separados, chegam à
/// mesma resposta: pendente nos primeiros <see cref="SegundosParaPagar"/>
/// segundos, paga depois, como o Pix do simulador do Pagar.me.
/// </summary>
public sealed class GatewaySimulado(IOptions<OpcoesPagarme> opcoes, IOptions<Application.OpcoesDaPlataforma> plataforma, TimeProvider relogio) : IGatewayDePagamento
{
    public const int SegundosParaPagar = 20;

    public string RecebedorDaAvalia => opcoes.Value.RecebedorAvalia;

    public Task<PedidoDoGateway> CriarPedido(PedidoNoGateway pedido, CancellationToken ct) =>
        Task.FromResult(Ler(pedido.Codigo) ?? throw new Application.IntegracaoException("Simulador", null, "código fora do formato da cobrança"));

    /// <summary>O link abre uma página da própria API em Development que avisa que o pagamento é simulado.</summary>
    public Task<LinkDoGateway> CriarLink(LinkNoGateway link, CancellationToken ct) =>
        Task.FromResult(new LinkDoGateway("pl_simulado_" + link.Codigo, $"{plataforma.Value.UrlPublica.TrimEnd('/')}/simulador/pagarme/{link.Codigo}"));

    public Task<PedidoDoGateway?> BuscarPorCodigo(string codigo, CancellationToken ct) => Task.FromResult(Ler(codigo));

    private PedidoDoGateway? Ler(string codigo)
    {
        if (!Guid.TryParseExact(codigo, "N", out var id) || id.Version != 7)
        {
            return null;
        }

        // Os 48 primeiros bits do UUID v7 são os milissegundos Unix da criação.
        var milissegundos = long.Parse(codigo[..12], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var criado = DateTimeOffset.FromUnixTimeMilliseconds(milissegundos);
        var paga = relogio.GetUtcNow() - criado >= TimeSpan.FromSeconds(SegundosParaPagar);
        return new PedidoDoGateway(
            "or_simulado_" + codigo,
            codigo,
            paga ? SituacaoDaCobranca.Paga : SituacaoDaCobranca.Pendente,
            paga ? criado.AddSeconds(SegundosParaPagar) : null,
            "00020101021226800014br.gov.bcb.pix2558simulador.avaliasales/" + codigo[..10] + "5204000053039865802BR5912AVALIA SALES6009SAO PAULO6304ABCD",
            null,
            criado.AddMinutes(opcoes.Value.ValidadeDoPixEmMinutos));
    }
}
