# 0001. Tenant no banco único com filtro do EF Core e RLS

- Situação - Aceita
- Data - 21/10/2026

## Contexto

A plataforma atende vários revendedores no mesmo banco. Um vazamento de dado de um revendedor para outro é o pior defeito possível do produto.

## Opções

- Banco por revendedor
- Esquema por revendedor
- Banco único com `revendedor_id`, filtro global do EF Core e RLS do PostgreSQL

## Decisão

Banco único com duas travas independentes: o filtro global do EF Core, montado por expressão para toda entidade `IDoRevendedor`, e o RLS com `FORCE ROW LEVEL SECURITY`, lendo `app.revendedor_id` gravado em cada conexão. A aplicação conecta com um papel sem superusuário e sem `BYPASSRLS`.

## Consequências

- Uma migration só para todos; consulta entre revendedores para a operadora é possível onde for explícita.
- Cada trava tem teste próprio, conferido desligando a trava.
- Toda tabela nova com `revendedor_id` precisa de `IsolarPorRevendedor`; um teste falha sem ele.
