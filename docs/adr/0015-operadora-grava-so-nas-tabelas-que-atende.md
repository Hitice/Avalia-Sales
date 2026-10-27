# 0015. Operadora grava só nas tabelas que atende

A operadora não tem revendedor no token. Nas tabelas que ela atende, ela grava
um registro em nome do revendedor certo; nas demais, a aplicação e o banco
recusam.

- Situação - Aceita
- Data - 21/10/2026
- Complementa - 0008

## Contexto

Nos chamados, a operadora responde na conversa de um revendedor; o mesmo vale
para o despacho do pedido de placas, que avisa o dono, e para a ordem de
serviço. Ela não tem revendedor no token, e o carimbo do EF exige o revendedor
da requisição em todo registro novo.

## Opções

- Uma conexão separada para a operadora, com papel que passa por cima do RLS
- Deixar a operadora informar o revendedor, só nas tabelas que ela atende

## Decisão

O carimbo aceita o revendedor informado só quando quem grava é a operadora e a
entidade é `IAtendidoPelaOperadora`. Para qualquer outro usuário, o revendedor
continua saindo do token, mesmo que o registro venha com outro. A política de
RLS da tabela atendida admite a operadora; nas demais, nem o banco aceita.

## Consequências

- Nenhum papel com `BYPASSRLS` na aplicação.
- Abrir uma tabela à operadora continua sendo decisão explícita: interface no
  Domain e `operadoraAtende` na migration.
- As tabelas só da operadora, sem `revendedor_id` (livro, despesas e
  fechamento dela, entre outras), têm política própria (ADR 0025).
- O teste
  `So_a_operadora_grava_em_nome_de_outro_revendedor_e_so_nas_tabelas_que_atende`
  cobre as três pontas: revendedor tentando gravar em outro (o carimbo devolve
  o registro ao dele), operadora gravando em tabela atendida e operadora
  barrada em tabela não atendida, no carimbo, antes do banco.
