using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AvaliaSales.Tests.Apoio;

/// <summary>Chamadas JSON dos testes de fluxo, com a falha mostrando o corpo da resposta.</summary>
public static class Chamadas
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static async Task<JsonElement> Postar(this HttpClient cliente, string rota, object corpo, HttpStatusCode esperado = HttpStatusCode.OK)
    {
        var resposta = await cliente.PostAsJsonAsync(rota, corpo, Json, TestContext.Current.CancellationToken);
        return await Ler(resposta, esperado);
    }

    public static async Task<JsonElement> Pegar(this HttpClient cliente, string rota, HttpStatusCode esperado = HttpStatusCode.OK) =>
        await Ler(await cliente.GetAsync(rota, TestContext.Current.CancellationToken), esperado);

    public static async Task<JsonElement> Colocar(this HttpClient cliente, string rota, object corpo, HttpStatusCode esperado = HttpStatusCode.OK) =>
        await Ler(await cliente.PutAsJsonAsync(rota, corpo, Json, TestContext.Current.CancellationToken), esperado);

    private static async Task<JsonElement> Ler(HttpResponseMessage resposta, HttpStatusCode esperado)
    {
        var texto = await resposta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(resposta.StatusCode == esperado, $"Esperava {(int)esperado}, veio {(int)resposta.StatusCode}: {texto}");
        return string.IsNullOrWhiteSpace(texto) ? default : JsonDocument.Parse(texto).RootElement.Clone();
    }
}
