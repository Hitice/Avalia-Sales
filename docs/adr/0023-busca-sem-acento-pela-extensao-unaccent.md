# 0023. Busca sem acento pela extensão unaccent

- Situação - Aceita
- Data - 26/10/2026

## Contexto

A busca das listas paginadas passou para a API e comparava com `lower()`, o que não
acha "otica" para "Ótica", ao contrário da busca local que ela substituiu. Os casos
de uso repetiam a comparação em dezenas de pontos.

## Opções

- Tirar o acento no C# e comparar com uma coluna normalizada gravada em cada tabela
- Normalizar só o termo no C#, sem tocar a coluna
- `unaccent` do PostgreSQL nos dois lados, por uma função do banco mapeada no EF Core

## Decisão

A terceira. A extensão `unaccent` é "trusted" desde o PG 13, então o dono do banco, que
roda as migrations, a cria sem superusuário; o papel `avalia_app` só executa. A migration
`BuscaSemAcento` cria `avalia_normalizar(text)`, que faz `lower(unaccent(dicionário, texto))`,
marcada `IMMUTABLE` por usar o dicionário explícito (a `unaccent(text)` simples é `STABLE`),
o que também permite um índice por expressão no futuro. No código, `Busca.Normalizar`
(`Application/Busca.cs`) é mapeada com `HasDbFunction` e é o único ponto usado pelas
buscas: `Busca.Normalizar(coluna).Contains(Busca.Normalizar(termo))`. O `Contains`
continua `strpos`, então `%` e `_` seguem como letras.

## Consequências

- Coluna e termo passam pela mesma regra, sem divergência entre C# e Postgres.
- A comparação não usa índice (`strpos` sobre expressão). As listas já filtram por revenda
  antes, e não houve medição que pedisse trigram; `pg_trgm` só entra se uma lista crescer a
  ponto de medir ganho e se a extensão for "trusted" na versão em uso.
- Enum buscado pelo nome (situação do ERP, ação da auditoria) continua comparado em C#, sem acento.
