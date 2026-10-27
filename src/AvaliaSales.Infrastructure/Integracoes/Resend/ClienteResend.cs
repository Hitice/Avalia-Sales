using System.Net.Http.Json;
using System.Net.Mail;
using System.Text.Json;
using System.Text.Json.Serialization;
using AvaliaSales.Application.Avisos;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Infrastructure.Integracoes.Resend;

/// <summary>
/// POST /emails do Resend, conferido na referência em 21/10/2026. A chave da
/// fila vai como Idempotency-Key (até 256 caracteres, vale 24 horas): uma
/// retentativa depois de queda não entrega em dobro.
/// </summary>
public sealed class ClienteResend(HttpClient http, IOptions<OpcoesResend> opcoes, IOptions<Application.OpcoesDaPlataforma> plataforma) : IEnvioDeEmail
{
    private const string Sistema = "Resend";

    private static readonly JsonSerializerOptions SemNulos = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public bool Configurado => !string.IsNullOrWhiteSpace(opcoes.Value.ChaveApi) && !string.IsNullOrWhiteSpace(opcoes.Value.Remetente);

    /// <summary>
    /// O remetente da revenda vai como nome de exibição sobre o endereço da
    /// plataforma ("Padaria via Avalia Sales &lt;avisos@...&gt;"); a resposta vai ao dono.
    /// </summary>
    public async Task<string> Enviar(MensagemDeEmail mensagem, CancellationToken ct)
    {
        var plataforma = new MailAddress(opcoes.Value.Remetente);
        var marca = string.IsNullOrWhiteSpace(plataforma.DisplayName) ? "Avalia Sales" : plataforma.DisplayName;
        var de = mensagem.Remetente is { } r ? $"\"{r.Nome} via {marca}\" <{plataforma.Address}>" : opcoes.Value.Remetente;
        var responder = mensagem.Remetente?.ResponderPara ?? NuloSeVazio(opcoes.Value.ResponderPara);
        using var pedido = new HttpRequestMessage(HttpMethod.Post, "emails")
        {
            Content = JsonContent.Create(new
            {
                from = de,
                to = new[] { mensagem.Para },
                subject = mensagem.Assunto,
                text = mensagem.Texto,
                html = ModeloDeEmail.Html(mensagem.Assunto, mensagem.Texto, mensagem.Remetente?.Nome, mensagem.Remetente?.Cor, UrlDoLogo, mensagem.Destaques, UrlDoPainel),
                reply_to = responder is null ? null : new[] { responder },
            }, options: SemNulos),
        };
        var chave = mensagem.Chave;
        pedido.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", opcoes.Value.ChaveApi);
        pedido.Headers.Add("Idempotency-Key", chave.Length <= 256 ? chave : chave[..256]);

        var resposta = await ChamadaExterna.Json(Sistema, () => http.SendAsync(pedido, ct), ct);
        return resposta.Texto("id") ?? throw new Application.IntegracaoException(Sistema, null, "resposta sem id");
    }

    /// <summary>O PNG do logo servido pelo próprio front (web/public/marca).</summary>
    private string UrlDoPainel => plataforma.Value.UrlDoPainel;

    private string UrlDoLogo => UrlDoPainel.TrimEnd('/') + "/marca/avaliasales-email.png";

    private static string? NuloSeVazio(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto;
}
