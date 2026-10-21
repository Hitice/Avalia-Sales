using System.Net.Http.Json;
using System.Text.Json;
using AvaliaSales.Application.Contratos;
using AvaliaSales.Domain.Contratos;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Infrastructure.Integracoes.D4Sign;

/// <summary>
/// Rotas da API v1 da D4Sign, conferidas na referência oficial em 21/10/2026.
/// A autenticação vai na URL (tokenAPI e cryptKey); por isso a URL nunca entra
/// em mensagem de erro nem em log.
/// </summary>
public sealed class ClienteD4Sign(HttpClient http, IOptions<OpcoesD4Sign> opcoes) : IAssinaturaEletronica
{
    private const string Sistema = "D4Sign";

    public async Task<string> EnviarAoCofre(string nome, byte[] pdf, CancellationToken ct)
    {
        var corpo = new { base64_binary_file = Convert.ToBase64String(pdf), mime_type = "application/pdf", name = nome };
        var resposta = await Post($"documents/{opcoes.Value.Cofre}/uploadbinary", corpo, ct);
        return resposta.Texto("uuid") ?? throw new Application.IntegracaoException(Sistema, null, "upload sem uuid");
    }

    public Task CadastrarWebhook(string documento, CancellationToken ct) =>
        Post($"documents/{documento}/webhooks", new { url = opcoes.Value.UrlDoWebhook }, ct);

    /// <summary>Assinatura padrão D4Sign (act 1 = assinar), signatário com CPF.</summary>
    public Task CadastrarSignatario(string documento, string email, CancellationToken ct) =>
        Post($"documents/{documento}/createlist", new
        {
            signers = new[]
            {
                new { email, act = "1", foreign = "0", certificadoicpbr = "0", assinatura_presencial = "0" },
            },
        }, ct);

    public Task EnviarParaAssinatura(string documento, string mensagem, CancellationToken ct) =>
        Post($"documents/{documento}/sendtosigner", new { message = mensagem, skip_email = "0", workflow = "0" }, ct);

    /// <summary>Fases da D4Sign: 4 Finalizado, 6 Cancelado; as demais são andamento.</summary>
    public async Task<SituacaoDoContrato?> LerSituacao(string documento, CancellationToken ct)
    {
        var resposta = await ChamadaExterna.Json(Sistema, () => http.GetAsync(Rota($"documents/{documento}"), ct), ct);
        var item = resposta.ValueKind == JsonValueKind.Array && resposta.GetArrayLength() > 0 ? resposta[0] : resposta;
        return item.Texto("statusId") switch
        {
            "4" => SituacaoDoContrato.Assinado,
            "6" => SituacaoDoContrato.Cancelado,
            _ => null,
        };
    }

    private Task<JsonElement> Post<T>(string caminho, T corpo, CancellationToken ct) =>
        ChamadaExterna.Json(Sistema, () => http.PostAsJsonAsync(Rota(caminho), corpo, ct), ct);

    private string Rota(string caminho)
    {
        var rota = $"{caminho}?tokenAPI={Uri.EscapeDataString(opcoes.Value.TokenApi)}";
        return string.IsNullOrEmpty(opcoes.Value.CryptKey) ? rota : $"{rota}&cryptKey={Uri.EscapeDataString(opcoes.Value.CryptKey)}";
    }
}
