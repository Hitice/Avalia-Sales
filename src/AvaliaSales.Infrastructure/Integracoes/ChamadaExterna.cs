using System.Text.Json;
using AvaliaSales.Application;
using Polly.Timeout;

namespace AvaliaSales.Infrastructure.Integracoes;

/// <summary>
/// O jeito único de chamar um sistema de fora: falha de rede, tempo esgotado
/// e resposta de erro viram IntegracaoException, com o corpo cortado e sem a
/// URL, que na D4Sign carrega a chave.
/// </summary>
internal static class ChamadaExterna
{
    private const int CorpoMaximoNoErro = 300;

    public static async Task<JsonElement> Json(string sistema, Func<Task<HttpResponseMessage>> chamada, CancellationToken ct)
    {
        HttpResponseMessage resposta;
        try
        {
            resposta = await chamada();
        }
        catch (HttpRequestException erro)
        {
            throw new IntegracaoException(sistema, null, $"falha de rede ({erro.HttpRequestError})");
        }
        catch (TimeoutRejectedException)
        {
            throw new IntegracaoException(sistema, null, "tempo esgotado");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new IntegracaoException(sistema, null, "tempo esgotado");
        }

        using (resposta)
        {
            var texto = await resposta.Content.ReadAsStringAsync(ct);
            if (!resposta.IsSuccessStatusCode)
            {
                throw new IntegracaoException(sistema, (int)resposta.StatusCode, texto.Length > CorpoMaximoNoErro ? texto[..CorpoMaximoNoErro] : texto);
            }

            if (string.IsNullOrWhiteSpace(texto))
            {
                return default;
            }

            try
            {
                using var documento = JsonDocument.Parse(texto);
                return documento.RootElement.Clone();
            }
            catch (JsonException)
            {
                throw new IntegracaoException(sistema, (int)resposta.StatusCode, "resposta não é JSON");
            }
        }
    }

    public static string? Texto(this JsonElement elemento, string propriedade) =>
        elemento.ValueKind == JsonValueKind.Object
        && elemento.TryGetProperty(propriedade, out var valor)
        && valor.ValueKind == JsonValueKind.String
            ? valor.GetString()
            : null;

    public static DateTimeOffset? Data(this JsonElement elemento, string propriedade) =>
        DateTimeOffset.TryParse(elemento.Texto(propriedade), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var data)
            ? data
            : null;
}
