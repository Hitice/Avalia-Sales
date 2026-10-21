using System.Net.Http.Json;
using AvaliaSales.Application.Erp;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Infrastructure.Integracoes.Protheus;

/// <summary>
/// O pedido de venda no Protheus, por um serviço REST publicado nele
/// (POST pedidos-de-venda), com autenticação básica, como o REST do Protheus
/// costuma ser exposto. O corpo é o contrato da plataforma; o serviço ADVPL do
/// lado da TOTVS mapeia para o pedido de venda e devolve o número gerado.
/// </summary>
public sealed class ClienteProtheus(HttpClient http, IOptions<OpcoesProtheus> opcoes) : IErp
{
    private const string Sistema = "Protheus";

    public bool Configurado => !string.IsNullOrWhiteSpace(opcoes.Value.UrlBase);

    public async Task<string> EnviarPedidoDeVenda(PedidoDeVendaNoErp pedido, CancellationToken ct)
    {
        using var mensagem = new HttpRequestMessage(HttpMethod.Post, "pedidos-de-venda") { Content = JsonContent.Create(pedido) };
        mensagem.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{opcoes.Value.Usuario}:{opcoes.Value.Senha}")));
        var resposta = await ChamadaExterna.Json(Sistema, () => http.SendAsync(mensagem, ct), ct);
        return resposta.Texto("numero") ?? throw new Application.IntegracaoException(Sistema, null, "resposta sem número do pedido");
    }
}
