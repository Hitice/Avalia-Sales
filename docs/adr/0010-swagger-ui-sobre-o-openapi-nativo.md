# 0010. Swagger UI sobre o OpenAPI nativo

Em desenvolvimento, a API publica o documento OpenAPI gerado pelo próprio
ASP.NET Core e uma página Swagger UI para consultar e testar as rotas. Em
produção, nenhum dos dois existe.

- Situação - Aceita
- Data - 21/10/2026

## Contexto

A API precisa de uma página para consultar e testar as rotas em
desenvolvimento, inclusive com o token de autorização.

## Opções

- Swashbuckle completo - um segundo gerador de documento, além do nativo
- OpenAPI nativo do ASP.NET Core com Scalar
- OpenAPI nativo com Swagger UI

## Decisão

`Microsoft.AspNetCore.OpenApi` gera o documento em `/openapi/v1.json`;
`Swashbuckle.AspNetCore.SwaggerUI` só desenha a página em `/swagger`. Os dois
só sobem em Development.

## Consequências

- Um gerador de documento só, o do framework.
- Página conhecida, com Authorize para o token.
- Em produção, o mapa das rotas e a página de testes não ficam expostos. O
  contrato que o front usa fica em `web/openapi.json`, gravado pelo
  `ContratoDaApiTest`, que falha no CI se o arquivo estiver desatualizado.
