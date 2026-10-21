# 0008. Operadora sem revendedor, com tabelas marcadas

- Situação - Aceita
- Data - 21/10/2026

## Contexto

A Avalia opera a plataforma e despacha os pedidos de todos os revendedores. Ela também é revendedora, com usuários separados.

## Opções

- Operadora como revendedor especial
- Papel de operadora sem revendedor, que abre só tabelas marcadas

## Decisão

Usuário com papel Operadora não tem revendedor (check no banco). Tabelas marcadas com `IAtendidoPelaOperadora` (hoje `pedidos_placas`) abrem para ela no filtro do EF e na política de RLS; as demais continuam fechadas.

## Consequências

- Nenhuma regra pergunta se o revendedor é a Avalia.
- Abrir uma tabela nova à operadora é decisão explícita no código e na migration.
