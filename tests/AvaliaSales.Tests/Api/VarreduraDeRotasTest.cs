using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Api;

/// <summary>
/// Varredura de todas as rotas pelo documento OpenAPI (disponível em Development, como nos testes):
/// nenhuma devolve 500 a corpo ausente, quebrado, nulo ou com valores extremos, e as rotas que não são
/// públicas recusam quem não tem o papel. Rota nova entra na varredura sozinha.
/// </summary>
[Collection(ColecaoDaApi.Nome)]
public sealed partial class VarreduraDeRotasTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Rotas sem login por desenho: cadastro, entrada, leitura da placa, webhooks e a foto do catálogo.</summary>
    private static readonly string[] Publicas =
    [
        "/cadastro", "/auth/", "/q/", "/l/", "/webhooks/", "/catalogo/{id}/foto", "/simulador",
    ];

    /// <summary>Rotas que um teste de varredura não pode disparar: trocam a própria senha ou o segundo fator do login de teste.</summary>
    private static readonly string[] Ignoradas =
    [
        "/auth/", "/webhooks/", "/simulador", "/conta/senha", "/conta/segundo-fator", "/revenda/encerramento",
    ];

    private static readonly string[] Variantes = ["texto-longo", "texto-vazio", "nulo", "negativo", "gigante", "zero"];

    [Fact]
    public async Task Nenhuma_rota_responde_500_a_corpo_ausente_quebrado_ou_extremo()
    {
        var revenda = await api.RevendaComEstoque("Revenda Varredura");
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var documento = await Documento();
        var erros = new List<string>();

        foreach (var (caminho, metodo, esquema) in Operacoes(documento).Where(o => !Ignoradas.Any(i => o.Caminho.Contains(i, StringComparison.Ordinal))))
        {
            var url = Url(caminho, revenda.Codigos[0]);
            var corpos = new List<(string Nome, string? Json)> { ("sem corpo", null) };
            if (esquema is not null)
            {
                corpos.AddRange(Variantes
                    .Select(v => (v, Gerar(documento, esquema, v, 0)?.ToJsonString())));
                corpos.Add(("vazio", "{}"));
                corpos.Add(("quebrado", "{\"a\":"));
            }

            foreach (var cliente in new[] { revenda.Dono, operadora })
            {
                foreach (var (nome, json) in corpos)
                {
                    using var pedido = new HttpRequestMessage(new HttpMethod(metodo), url);
                    if (json is not null)
                    {
                        pedido.Content = new StringContent(json, Encoding.UTF8, "application/json");
                    }

                    using var resposta = await cliente.SendAsync(pedido, Ct);
                    if ((int)resposta.StatusCode >= 500)
                    {
                        erros.Add($"{(int)resposta.StatusCode} {metodo} {caminho} [{nome}]");
                    }
                }
            }
        }

        Assert.True(erros.Count == 0, string.Join(Environment.NewLine, erros.Distinct()));
    }

    [Fact]
    public async Task Rota_que_nao_e_publica_recusa_anonimo_e_rota_da_operadora_recusa_vendedor()
    {
        var revenda = await api.RevendaComEstoque("Revenda Matriz");
        var email = $"vendedor-{Guid.NewGuid():N}@teste.com.br";
        await revenda.Dono.Postar("/equipe", new { nome = "Lia", email, senha = ApiDeTeste.Senha }, HttpStatusCode.Created);
        var vendedor = await api.ClienteLogado(email);
        var anonimo = api.Fabrica.CreateClient();
        var erros = new List<string>();

        foreach (var (caminho, metodo, _) in Operacoes(await Documento()))
        {
            var url = Url(caminho, "ABC234");
            if (!Publicas.Any(p => caminho.Contains(p, StringComparison.Ordinal)))
            {
                erros.AddRange(await Conferir(anonimo, metodo, url, "anônimo", caminho, HttpStatusCode.Unauthorized));
            }

            // A foto do catálogo exige o Content-Type certo antes da autorização; tem teste próprio.
            if (caminho.StartsWith("/operadora/", StringComparison.Ordinal) && !caminho.EndsWith("/foto", StringComparison.Ordinal))
            {
                erros.AddRange(await Conferir(vendedor, metodo, url, "vendedor", caminho, HttpStatusCode.Forbidden));
            }
        }

        Assert.True(erros.Count == 0, string.Join(Environment.NewLine, erros));
    }

    private static async Task<List<string>> Conferir(HttpClient cliente, string metodo, string url, string quem, string caminho, HttpStatusCode esperado)
    {
        using var pedido = new HttpRequestMessage(new HttpMethod(metodo), url) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        using var resposta = await cliente.SendAsync(pedido, Ct);
        return resposta.StatusCode == esperado ? [] : [$"{quem} {metodo} {caminho}: {(int)resposta.StatusCode}, esperava {(int)esperado}"];
    }

    private async Task<JsonNode> Documento() =>
        JsonNode.Parse(await api.Fabrica.CreateClient().GetStringAsync("/openapi/v1.json", Ct))!;

    private static IEnumerable<(string Caminho, string Metodo, JsonNode? Esquema)> Operacoes(JsonNode documento) =>
        documento["paths"]!.AsObject().SelectMany(p => p.Value!.AsObject().Select(o =>
            (p.Key, o.Key.ToUpperInvariant(), o.Value!["requestBody"]?["content"]?["application/json"]?["schema"])));

    private static string Url(string caminho, string codigoDePlaca) =>
        Parametro().Replace(caminho, m => m.Groups[1].Value.Contains("id", StringComparison.OrdinalIgnoreCase) ? Guid.NewGuid().ToString() : codigoDePlaca);

    [GeneratedRegex(@"\{(\w+)(:\w+)?\}")]
    private static partial Regex Parametro();

    /// <summary>Um corpo para o esquema, com o valor extremo da variante em cada campo.</summary>
    private static JsonNode? Gerar(JsonNode documento, JsonNode? esquema, string variante, int profundidade)
    {
        if (esquema is null || profundidade > 6)
        {
            return null;
        }

        if (esquema["$ref"] is { } referencia)
        {
            return Gerar(documento, documento["components"]!["schemas"]![referencia.GetValue<string>().Split('/')[^1]], variante, profundidade + 1);
        }

        foreach (var alternativa in new[] { "oneOf", "anyOf" })
        {
            if (esquema[alternativa] is JsonArray lista)
            {
                return Gerar(documento, lista.FirstOrDefault(x => x?["type"]?.ToString() != "null") ?? lista[0], variante, profundidade + 1);
            }
        }

        if (esquema["enum"] is JsonArray valores && valores.Count > 0 && variante != "nulo")
        {
            return valores[0]!.DeepClone();
        }

        var tipo = esquema["type"] is JsonArray tipos ? tipos.Select(t => t!.ToString()).FirstOrDefault(t => t != "null") : esquema["type"]?.ToString();
        var formato = esquema["format"]?.ToString();
        switch (tipo)
        {
            case "object":
                var objeto = new JsonObject();
                foreach (var (nome, filho) in esquema["properties"]?.AsObject() ?? [])
                {
                    objeto[nome] = variante == "nulo" ? null : Gerar(documento, filho, variante, profundidade + 1);
                }

                return objeto;
            case "array":
                return new JsonArray(Gerar(documento, esquema["items"], variante, profundidade + 1));
            case "string":
                return JsonValue.Create(formato switch
                {
                    "uuid" => variante == "texto-vazio" ? Guid.Empty.ToString() : Guid.NewGuid().ToString(),
                    "date" => variante == "negativo" ? "0001-01-01" : variante == "gigante" ? "9999-12-31" : "2026-10-25",
                    "date-time" => variante == "negativo" ? "0001-01-01T00:00:00Z" : variante == "gigante" ? "9999-12-31T23:59:59Z" : "2026-10-25T10:00:00Z",
                    _ => variante switch
                    {
                        "texto-longo" => new string('A', 5000),
                        "gigante" => new string('é', 200_000),
                        "texto-vazio" => string.Empty,
                        "negativo" => "-1",
                        _ => "x",
                    },
                });
            case "integer" or "number":
                return JsonValue.Create(variante switch
                {
                    "negativo" => -1L,
                    "gigante" => formato == "int32" ? int.MaxValue : long.MaxValue,
                    "zero" => 0L,
                    _ => 1L,
                });
            case "boolean":
                return JsonValue.Create(true);
            default:
                return null;
        }
    }
}
