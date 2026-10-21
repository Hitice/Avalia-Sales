using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace AvaliaSales.Api.Autenticacao;

/// <summary>Declara o token no documento OpenAPI, para a página de rotas mandar o Bearer nas rotas protegidas.</summary>
internal sealed class EsquemaDoToken : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument documento, OpenApiDocumentTransformerContext contexto, CancellationToken cancellationToken)
    {
        documento.Components ??= new OpenApiComponents();
        documento.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        documento.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Token de POST /auth/entrar",
        };
        documento.Security ??= [];
        documento.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", documento)] = [] });
        return Task.CompletedTask;
    }
}
