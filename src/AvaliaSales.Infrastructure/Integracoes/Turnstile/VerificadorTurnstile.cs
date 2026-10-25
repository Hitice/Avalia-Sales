using System.Text.Json;
using AvaliaSales.Application.Seguranca;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Infrastructure.Integracoes.Turnstile;

/// <summary>
/// Verificação no servidor (POST siteverify da Cloudflare, form com secret,
/// response e remoteip). Falha fechada: resposta que não é um JSON de sucesso ou
/// de reprovação claras, tempo esgotado e erro de rede viram Indisponivel, e o
/// cadastro é recusado. Sem chave configurada, aprova sem chamar a rede.
/// </summary>
public sealed partial class VerificadorTurnstile(HttpClient http, IOptions<OpcoesTurnstile> opcoes, ILogger<VerificadorTurnstile> log) : IVerificadorDeCaptcha
{
    /// <summary>O limite de tamanho do token que a Cloudflare documenta.</summary>
    private const int TokenMaximo = 2048;

    /// <summary>Códigos de erro que são do nosso lado ou da Cloudflare, e não do visitante.</summary>
    private static readonly string[] ErrosDoServico = ["missing-input-secret", "invalid-input-secret", "bad-request", "internal-error"];

    public bool Ligado => opcoes.Value.Ligado;

    public async Task<ResultadoDoCaptcha> Verificar(string? token, string? ip, CancellationToken ct)
    {
        if (!Ligado)
        {
            return ResultadoDoCaptcha.Aprovado;
        }

        if (string.IsNullOrWhiteSpace(token) || token.Length > TokenMaximo)
        {
            return ResultadoDoCaptcha.Reprovado;
        }

        var campos = new List<KeyValuePair<string, string>> { new("secret", opcoes.Value.ChaveSecreta), new("response", token) };
        if (!string.IsNullOrWhiteSpace(ip))
        {
            campos.Add(new("remoteip", ip));
        }

        try
        {
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(TimeSpan.FromSeconds(opcoes.Value.TempoLimiteEmSegundos));
            using var resposta = await http.PostAsync(opcoes.Value.UrlDeVerificacao, new FormUrlEncodedContent(campos), limite.Token);
            if (!resposta.IsSuccessStatusCode)
            {
                LogIndisponivel(log, (int)resposta.StatusCode);
                return ResultadoDoCaptcha.Indisponivel;
            }

            using var json = await JsonDocument.ParseAsync(await resposta.Content.ReadAsStreamAsync(limite.Token), cancellationToken: limite.Token);
            if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("success", out var sucesso)
                || sucesso.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                LogIndisponivel(log, (int)resposta.StatusCode);
                return ResultadoDoCaptcha.Indisponivel;
            }

            if (sucesso.GetBoolean())
            {
                return ResultadoDoCaptcha.Aprovado;
            }

            var erros = json.RootElement.TryGetProperty("error-codes", out var lista) && lista.ValueKind == JsonValueKind.Array
                ? lista.EnumerateArray().Select(e => e.GetString()).ToList()
                : [];
            if (erros.Any(e => e is not null && ErrosDoServico.Contains(e)))
            {
                LogIndisponivel(log, 0);
                return ResultadoDoCaptcha.Indisponivel;
            }

            return ResultadoDoCaptcha.Reprovado;
        }
        catch (Exception erro) when ((erro is HttpRequestException or JsonException or OperationCanceledException) && !ct.IsCancellationRequested)
        {
            LogFalha(log, erro);
            return ResultadoDoCaptcha.Indisponivel;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Turnstile indisponível ou mal configurado (status {Status}); cadastro recusado")]
    private static partial void LogIndisponivel(ILogger log, int status);

    [LoggerMessage(Level = LogLevel.Error, Message = "Turnstile não respondeu; cadastro recusado")]
    private static partial void LogFalha(ILogger log, Exception erro);
}
