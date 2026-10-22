using System.Net.Http.Json;
using System.Text.Json;
using AvaliaSales.Application.Lugares;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Infrastructure.Integracoes.Google;

/// <summary>
/// Text Search (New): POST places:searchText, conferido na referência em
/// 21/10/2026. A máscara pede só id, nome e endereço (SKU Text Search Pro).
/// A chave fica no servidor; o front não fala com o Places.
/// </summary>
public sealed class ClientePlaces(HttpClient http, IOptions<OpcoesGoogle> opcoes) : ILugares
{
    private const string Sistema = "Google Places";
    private const int Resultados = 8;

    /// <summary>O método do Text Search (New), relativo à base configurada em Google:UrlDoPlaces.</summary>
    public const string Metodo = "./places:searchText";

    public bool Configurado => !string.IsNullOrWhiteSpace(opcoes.Value.ChaveDoServidor);

    public async Task<IReadOnlyList<Lugar>> Buscar(string texto, CancellationToken ct)
    {
        // "./" na frente: sozinho, "places:searchText" é lido como endereço absoluto de
        // esquema "places:" e a chamada nem sai. Assim resolve sobre a base (/v1/).
        using var pedido = new HttpRequestMessage(HttpMethod.Post, Metodo)
        {
            Content = JsonContent.Create(new { textQuery = texto, languageCode = "pt-BR", regionCode = "BR", pageSize = Resultados }),
        };
        pedido.Headers.Add("X-Goog-Api-Key", opcoes.Value.ChaveDoServidor);
        pedido.Headers.Add("X-Goog-FieldMask", "places.id,places.displayName,places.formattedAddress");

        var resposta = await ChamadaExterna.Json(Sistema, () => http.SendAsync(pedido, ct), ct);
        if (!resposta.TryGetProperty("places", out var lugares) || lugares.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return lugares.EnumerateArray()
            .Select(l => new Lugar(
                l.Texto("id") ?? string.Empty,
                l.TryGetProperty("displayName", out var nome) ? nome.Texto("text") ?? string.Empty : string.Empty,
                l.Texto("formattedAddress")))
            .Where(l => l.Id.Length > 0)
            .ToList();
    }
}
