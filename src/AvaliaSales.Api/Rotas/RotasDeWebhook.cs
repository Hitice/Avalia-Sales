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
    /// A D4Sign manda formulário com uuid, type_post e email, e o HMAC do uuid
    /// no cabeçalho Content-Hmac. Não há id de evento: a chave é
    /// uuid, tipo e e-mail, que não se repetem para o mesmo fato.
    /// </summary>
    private static async Task<IResult> D4Sign(
        HttpRequest requisicao, IOptions<OpcoesD4Sign> opcoes, CaixaDeEntrada caixa, CancellationToken ct)
    {
        if (!requisicao.HasFormContentType)
        {
            return Results.BadRequest();
        }

        var formulario = await requisicao.ReadFormAsync(ct);
        var uuid = formulario["uuid"].ToString();
        var tipo = formulario["type_post"].ToString();
        var email = formulario["email"].ToString();

        if (!AssinaturaDoWebhook.Valida(uuid, requisicao.Headers["Content-Hmac"], opcoes.Value.SegredoHmac))
        {
            return Results.Unauthorized();
        }

        var corpo = JsonSerializer.Serialize(formulario.ToDictionary(campo => campo.Key, campo => campo.Value.ToString()));
        await caixa.Registrar(VinculoExterno.D4Sign, $"{uuid}:{tipo}:{email}", $"type_post {tipo}", uuid, corpo, ct);
        return Results.Ok();
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
