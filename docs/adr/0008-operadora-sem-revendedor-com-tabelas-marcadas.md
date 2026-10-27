# 0008. Operadora sem revendedor, com tabelas marcadas

A Avalia opera a plataforma com usuários de papel Operadora, que não pertencem a
nenhum revendedor e só veem as tabelas marcadas para eles. Como revendedora, a
Avalia tem outros usuários e segue as regras de qualquer revenda.

- Situação - Aceita
- Data - 21/10/2026

## Contexto

A Avalia opera a plataforma e despacha os pedidos de todos os revendedores. Ela
também é revendedora, com usuários separados.

## Opções

- Operadora como revendedor especial - toda regra teria de perguntar se o
  revendedor é a Avalia
- Papel de operadora sem revendedor, que abre só tabelas marcadas

## Decisão

Usuário com papel Operadora não tem revendedor (check
`ck_usuarios_papel_e_revendedor` no banco). As tabelas das entidades marcadas
com `IAtendidoPelaOperadora` abrem para ela no filtro do EF e na política de
RLS (`operadoraAtende` em `IsolarPorRevendedor`); as demais continuam fechadas.
Em 27/10/2026 são cinco: `pedidos_placas`, `chamados`, `mensagens`,
`notificacoes` e `ordens_de_servico`. A lista vale pelo código: é quem
implementa a interface no Domain.

## Consequências

- Nenhuma regra pergunta se o revendedor é a Avalia. Como revendedora, a Avalia
  segue as regras de qualquer revenda: o dono pode vender, a venda dele não tem
  comissão a dividir, e o que sobra da venda é dele.
- Abrir uma tabela nova à operadora é decisão explícita no código e na
  migration (ADR 0015).
- As tabelas só da operadora, sem `revendedor_id`, têm política própria (ADR
  0025).
