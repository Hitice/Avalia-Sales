using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AvaliaSales.Domain.Integracoes;
using AvaliaSales.Infrastructure.Integracoes;
using AvaliaSales.Infrastructure.Integracoes.D4Sign;
using AvaliaSales.Infrastructure.Integracoes.Pagarme;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Api.Rotas;

/// <summary>
/// Webhooks só gravam na caixa de entrada e respondem 200; o Worker processa
/// depois, reconsultando a fonte (skill idempotencia). Nada aqui muda cobrança
/// ou contrato.
/// </summary>
public static class RotasDeWebhook
{
    public const string LimiteDeWebhook = "webhook";
    private const int CorpoMaximoBytes = 1024 * 1024;

    public static void MapearWebhooks(this IEndpointRouteBuilder rotas)
    {
        var webhooks = rotas.MapGroup("/webhooks").AllowAnonymous().RequireRateLimiting(LimiteDeWebhook);
        webhooks.MapPost("/pagarme/{segredo}", Pagarme);
        webhooks.MapPost("/d4sign", D4Sign);
    }

    /// <summary>
    /// O Pagar.me não assina o webhook. O segredo na URL barra quem não a
    /// conhece, e responde 404 para não confirmar que a rota existe.
    /// </summary>
    private static async Task<IResult> Pagarme(
        string segredo, HttpRequest requisicao, IOptions<OpcoesPagarme> opcoes, CaixaDeEntrada caixa, CancellationToken ct)
    {
        if (!Iguais(segredo, opcoes.Value.SegredoDoWebhook))
        {
            return Results.NotFound();
        }

        var corpo = await LerCorpo(requisicao, ct);
        if (corpo is null)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        string? id, tipo, codigo;
        try
        {
            using var documento = JsonDocument.Parse(corpo);
            var raiz = documento.RootElement;
            id = Texto(raiz, "id");
            tipo = Texto(raiz, "type") ?? Texto(raiz, "event");
            codigo = raiz.TryGetProperty("data", out var dados) ? Texto(dados, "code") : null;
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }

        if (id is null || tipo is null)
        {
            return Results.BadRequest();
        }

        await caixa.Registrar(VinculoExterno.Pagarme, id, tipo, codigo ?? string.Empty, corpo, ct);
        return Results.Ok();
    }

    /// <summary>
    /// A D4Sign manda uuid, type_post e email, em formulário ou em JSON, conforme
    /// a conta (o painel deixa escolher), e o HMAC-SHA256 do uuid no cabeçalho
    /// Content-Hmac. No webhook 2.0, o e-mail vem em signer.email. Não há id de
    /// evento: a chave é uuid, tipo e e-mail, que não se repetem para o mesmo fato.
    /// </summary>
    private static async Task<IResult> D4Sign(
        HttpRequest requisicao, IOptions<OpcoesD4Sign> opcoes, CaixaDeEntrada caixa, CancellationToken ct)
    {
        string uuid, tipo, email, corpo;
        if (requisicao.HasFormContentType)
        {
            var formulario = await requisicao.ReadFormAsync(ct);
            uuid = formulario["uuid"].ToString();
            tipo = formulario["type_post"].ToString();
            email = formulario["email"].ToString();
            corpo = JsonSerializer.Serialize(formulario.ToDictionary(campo => campo.Key, campo => campo.Value.ToString()));
        }
        else if (requisicao.HasJsonContentType() && await LerCorpo(requisicao, ct) is { } json && LerEventoJson(json) is { } evento)
        {
            (uuid, tipo, email) = evento;
            corpo = json;
        }
        else
        {
            return Results.BadRequest();
        }

        if (!AssinaturaDoWebhook.Valida(uuid, requisicao.Headers["Content-Hmac"], opcoes.Value.SegredoHmac))
        {
            return Results.Unauthorized();
        }

        await caixa.Registrar(VinculoExterno.D4Sign, $"{uuid}:{tipo}:{email}", $"type_post {tipo}", uuid, corpo, ct);
        return Results.Ok();
    }

    /// <summary>uuid, type_post e o e-mail (direto no 1.0, em signer no 2.0); nulo se o JSON não for desse formato.</summary>
    private static (string Uuid, string Tipo, string Email)? LerEventoJson(string json)
    {
        try
        {
            using var documento = JsonDocument.Parse(json);
            var raiz = documento.RootElement;
            string Texto(JsonElement e, string nome) =>
                e.ValueKind == JsonValueKind.Object && e.TryGetProperty(nome, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : v.ToString() : string.Empty;
            var email = Texto(raiz, "email");
            if (email.Length == 0 && raiz.ValueKind == JsonValueKind.Object && raiz.TryGetProperty("signer", out var assinante))
            {
                email = Texto(assinante, "email");
            }

            return (Texto(raiz, "uuid"), Texto(raiz, "type_post"), email);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<string?> LerCorpo(HttpRequest requisicao, CancellationToken ct)
    {
        if (requisicao.ContentLength > CorpoMaximoBytes)
        {
            return null;
        }

        using var leitor = new StreamReader(requisicao.Body, Encoding.UTF8);
        var buffer = new char[CorpoMaximoBytes + 1];
        var lidos = await leitor.ReadBlockAsync(buffer.AsMemory(), ct);
        return lidos > CorpoMaximoBytes ? null : new string(buffer, 0, lidos);
    }

    private static string? Texto(JsonElement elemento, string propriedade) =>
        elemento.ValueKind == JsonValueKind.Object && elemento.TryGetProperty(propriedade, out var valor) && valor.ValueKind == JsonValueKind.String
            ? valor.GetString()
            : null;

    private static bool Iguais(string recebido, string esperado) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(recebido), Encoding.UTF8.GetBytes(esperado));
}
