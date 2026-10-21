# 0010. Swagger UI sobre o OpenAPI nativo

- Situação - Aceita
- Data - 21/10/2026

## Contexto

A API precisa de uma página de rotas em desenvolvimento.

## Opções

- Swashbuckle completo
- OpenAPI nativo do ASP.NET Core com Scalar
- OpenAPI nativo com Swagger UI

## Decisão

`Microsoft.AspNetCore.OpenApi` gera o documento em `/openapi/v1.json`; `Swashbuckle.AspNetCore.SwaggerUI` só desenha a página em `/swagger`. Só em Development.

## Consequências

- Um gerador de documento só, o do framework.
- Página conhecida, com Authorize para o token.
