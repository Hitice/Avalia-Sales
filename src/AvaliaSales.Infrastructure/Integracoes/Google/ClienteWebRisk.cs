using System.Text.Json;
using AvaliaSales.Application.Links;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Infrastructure.Integracoes.Google;

public sealed class OpcoesWebRisk
{
    public const string Secao = "WebRisk";

    /// <summary>Chave do Google Cloud restrita à Web Risk API, em WebRisk__ChaveApi. Vazia, a consulta fica desligada.</summary>
    public string ChaveApi { get; init; } = string.Empty;

    public string UrlDaConsulta { get; init; } = "https://webrisk.googleapis.com/v1/uris:search";

    public int TempoLimiteEmSegundos { get; init; } = 3;

    public bool Ligado => !string.IsNullOrWhiteSpace(ChaveApi);
}

/// <summary>
/// Lookup API do Web Risk (<c>GET uris:search</c>, ADR 0031): resposta <c>{}</c> é endereço fora das listas; com
/// <c>threat.threatTypes</c>, perigoso. Falha aberta: erro, tempo esgotado ou resposta estranha aprovam e vão ao log,
/// porque a venda não pode parar pelo Google. A chave vai no cabeçalho, e não na URL, para não aparecer em log.
/// A resposta fica guardada por endereço (a ameaça até o <c>expireTime</c>, o endereço limpo por 10 minutos): o
/// mesmo pedido repetido não volta ao Google.
/// </summary>
public sealed partial class ClienteWebRisk(HttpClient http, IOptions<OpcoesWebRisk> opcoes, IMemoryCache guardadas, TimeProvider relogio,
    ILogger<ClienteWebRisk> log) : IReputacaoDeEnderecos
{
    private static readonly string[] Ameacas = ["MALWARE", "SOCIAL_ENGINEERING", "UNWANTED_SOFTWARE"];

    private static readonly TimeSpan LimpoPor = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan AmeacaPorNoMaximo = TimeSpan.FromHours(24);

    public async Task<bool> Perigoso(string endereco, CancellationToken ct)
    {
        if (!opcoes.Value.Ligado)
        {
            return false;
        }

        var chave = $"webrisk:{endereco}";
        if (guardadas.TryGetValue(chave, out bool guardada))
        {
            return guardada;
        }

        if (await Consultar(endereco, ct) is not { } resposta)
        {
            return false;
        }

        var validade = resposta.Ate is { } ate && ate > relogio.GetUtcNow() ? ate - relogio.GetUtcNow() : LimpoPor;
        guardadas.Set(chave, resposta.Perigoso, resposta.Perigoso && validade > AmeacaPorNoMaximo ? AmeacaPorNoMaximo : validade);
        return resposta.Perigoso;
    }

    /// <summary>Nulo quando o Google não deu resposta que se possa usar; nada fica guardado.</summary>
    private async Task<(bool Perigoso, DateTimeOffset? Ate)?> Consultar(string endereco, CancellationToken ct)
    {
        var consulta = string.Join('&', Ameacas.Select(a => $"threatTypes={a}")) + $"&uri={Uri.EscapeDataString(endereco)}";
        using var pedido = new HttpRequestMessage(HttpMethod.Get, $"{opcoes.Value.UrlDaConsulta}?{consulta}");
        pedido.Headers.Add("X-Goog-Api-Key", opcoes.Value.ChaveApi);
        try
        {
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(TimeSpan.FromSeconds(opcoes.Value.TempoLimiteEmSegundos));
            using var resposta = await http.SendAsync(pedido, limite.Token);
            if (!resposta.IsSuccessStatusCode)
            {
                LogIndisponivel(log, (int)resposta.StatusCode);
                return null;
            }

            using var json = await JsonDocument.ParseAsync(await resposta.Content.ReadAsStreamAsync(limite.Token), cancellationToken: limite.Token);
            var raiz = json.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object)
            {
                LogIndisponivel(log, (int)resposta.StatusCode);
                return null;
            }

            if (!raiz.TryGetProperty("threat", out var ameaca))
            {
                return (false, null);
            }

            if (ameaca.ValueKind != JsonValueKind.Object || !ameaca.TryGetProperty("threatTypes", out var tipos) || tipos.ValueKind != JsonValueKind.Array)
            {
                LogIndisponivel(log, (int)resposta.StatusCode);
                return null;
            }

            DateTimeOffset? ate = ameaca.TryGetProperty("expireTime", out var expira) && expira.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(expira.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var data)
                ? data
                : null;
            return (tipos.GetArrayLength() > 0, ate);
        }
        catch (Exception erro) when (erro is HttpRequestException or JsonException || (erro is OperationCanceledException && !ct.IsCancellationRequested))
        {
            LogFalha(log, erro);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Web Risk respondeu {Status}; destino aceito sem a consulta")]
    private static partial void LogIndisponivel(ILogger log, int status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Web Risk não respondeu; destino aceito sem a consulta")]
    private static partial void LogFalha(ILogger log, Exception erro);
}
