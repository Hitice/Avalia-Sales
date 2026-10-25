using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace AvaliaSales.Api;

/// <summary>
/// Data com hora que chega do cliente vira UTC na entrada. O Npgsql só grava
/// timestamptz com deslocamento zero, e "2031-01-01T10:00:00-03:00" derrubava a
/// gravação com 500. O instante é o mesmo; só o deslocamento muda. A saída
/// continua como era (o banco já devolve UTC).
/// </summary>
public sealed class DataEmUtc : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader leitor, Type tipo, JsonSerializerOptions opcoes) =>
        leitor.GetDateTimeOffset().ToUniversalTime();

    public override void Write(Utf8JsonWriter escritor, DateTimeOffset valor, JsonSerializerOptions opcoes) =>
        escritor.WriteStringValue(valor);
}

/// <summary>
/// Um conversor próprio apaga o <c>type</c> do esquema da data no documento
/// OpenAPI, e o front gera os tipos dele. Devolve o esquema que o tipo
/// padrão tinha: string com formato date-time (e nulo, quando o campo aceita).
/// </summary>
internal sealed class DataEmUtcNoEsquema : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema esquema, OpenApiSchemaTransformerContext contexto, CancellationToken cancellationToken)
    {
        var tipo = contexto.JsonTypeInfo.Type;
        if (tipo == typeof(DateTimeOffset))
        {
            esquema.Type = JsonSchemaType.String;
            esquema.Format = "date-time";
        }
        else if (tipo == typeof(DateTimeOffset?))
        {
            esquema.Type = JsonSchemaType.String | JsonSchemaType.Null;
            esquema.Format = "date-time";
        }

        return Task.CompletedTask;
    }
}
