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

    /// <summary>
    /// Idempotente: se um envio anterior chegou à D4Sign e a resposta se perdeu (tempo
    /// esgotado), a nova tentativa ouve "already in the signature phase". O documento já
    /// está com os signatários, então o passo conta como feito.
    /// </summary>
    public async Task EnviarParaAssinatura(string documento, string mensagem, CancellationToken ct)
    {
        try
        {
            await Post($"documents/{documento}/sendtosigner", new { message = mensagem, skip_email = "0", workflow = "0" }, ct);
        }
        catch (Application.IntegracaoException erro) when (JaEmAssinatura(erro))
        {
        }
    }

    public static bool JaEmAssinatura(Application.IntegracaoException erro) =>
        erro.Status == 400 && erro.Message.Contains("already in the signature phase", StringComparison.OrdinalIgnoreCase);

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

    /// <summary>
    /// Reenvio do link (POST documents/{uuid}/resend, com e-mail e key_signer), conferido em
    /// docapi.d4sign.com.br em 22/10/2026. A chave do signatário sai da lista do documento.
    /// A D4Sign limita os reenvios (1, 5 e 30 minutos, depois 1 hora) e recusa os de antes.
    /// </summary>
    public async Task ReenviarAssinatura(string documento, string email, CancellationToken ct)
    {
        var lista = await ChamadaExterna.Json(Sistema, () => http.GetAsync(Rota($"documents/{documento}/list"), ct), ct);
        var signatarios = Signatarios(lista);
        var chave = signatarios.FirstOrDefault(s => string.Equals(s.Texto("email"), email, StringComparison.OrdinalIgnoreCase)).Texto("key_signer")
            ?? signatarios.Select(s => s.Texto("key_signer")).FirstOrDefault(k => k is not null)
            ?? throw new Application.IntegracaoException(Sistema, null, "documento sem signatário");
        await Post($"documents/{documento}/resend", new { email, key_signer = chave }, ct);
    }

    /// <summary>
    /// POST documents/{uuid}/download (type PDF) devolve uma URL temporária; o arquivo vem
    /// dela, e só de endereço da própria D4Sign.
    /// </summary>
    public async Task<byte[]> Baixar(string documento, CancellationToken ct)
    {
        var resposta = await Post($"documents/{documento}/download", new { type = "PDF", language = "pt" }, ct);
        var endereco = resposta.Texto("url") ?? throw new Application.IntegracaoException(Sistema, null, "download sem url");
        if (!Uri.TryCreate(endereco, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !(uri.Host == "d4sign.com.br" || uri.Host.EndsWith(".d4sign.com.br", StringComparison.OrdinalIgnoreCase)))
        {
            throw new Application.IntegracaoException(Sistema, null, "download fora da D4Sign");
        }

        using var arquivo = await http.GetAsync(uri, ct);
        return arquivo.IsSuccessStatusCode
            ? await arquivo.Content.ReadAsByteArrayAsync(ct)
            : throw new Application.IntegracaoException(Sistema, (int)arquivo.StatusCode, "download do arquivo recusado");
    }

    /// <summary>A lista vem como um objeto em "list" num documento com um signatário; com vários, pode vir em array.</summary>
    private static List<JsonElement> Signatarios(JsonElement resposta)
    {
        var raiz = resposta.ValueKind == JsonValueKind.Array && resposta.GetArrayLength() > 0 ? resposta[0] : resposta;
        if (!raiz.TryGetProperty("list", out var lista))
        {
            return [];
        }

        return lista.ValueKind == JsonValueKind.Array ? lista.EnumerateArray().ToList() : [lista];
    }

    private Task<JsonElement> Post<T>(string caminho, T corpo, CancellationToken ct) =>
        ChamadaExterna.Json(Sistema, () => http.PostAsJsonAsync(Rota(caminho), corpo, ct), ct);

    private string Rota(string caminho)
    {
        var rota = $"{caminho}?tokenAPI={Uri.EscapeDataString(opcoes.Value.TokenApi)}";
        return string.IsNullOrEmpty(opcoes.Value.CryptKey) ? rota : $"{rota}&cryptKey={Uri.EscapeDataString(opcoes.Value.CryptKey)}";
    }
}
