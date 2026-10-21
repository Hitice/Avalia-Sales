using System.Net.Http.Json;
using AvaliaSales.Application.Avisos;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Infrastructure.Integracoes.Resend;

/// <summary>
/// POST /emails do Resend, conferido na referência em 21/10/2026. A chave da
/// fila vai como Idempotency-Key (até 256 caracteres, vale 24 horas): uma
/// retentativa depois de queda não entrega em dobro.
/// </summary>
public sealed class ClienteResend(HttpClient http, IOptions<OpcoesResend> opcoes) : IEnvioDeEmail
{
    private const string Sistema = "Resend";

    public bool Configurado => !string.IsNullOrWhiteSpace(opcoes.Value.ChaveApi) && !string.IsNullOrWhiteSpace(opcoes.Value.Remetente);

    public async Task<string> Enviar(string chave, string para, string assunto, string texto, CancellationToken ct)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Post, "emails")
        {
            Content = JsonContent.Create(new { from = opcoes.Value.Remetente, to = new[] { para }, subject = assunto, text = texto }),
        };
        pedido.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", opcoes.Value.ChaveApi);
        pedido.Headers.Add("Idempotency-Key", chave.Length <= 256 ? chave : chave[..256]);

        var resposta = await ChamadaExterna.Json(Sistema, () => http.SendAsync(pedido, ct), ct);
        return resposta.Texto("id") ?? throw new Application.IntegracaoException(Sistema, null, "resposta sem id");
    }
}
