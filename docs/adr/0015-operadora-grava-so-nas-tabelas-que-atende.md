# 0015. Operadora grava só nas tabelas que atende

- Situação - Aceita
- Data - 21/10/2026
- Complementa - 0008

## Contexto

No chat, a operadora responde na conversa de um revendedor. Ela não tem
revendedor no token, e o carimbo do EF exige o revendedor da requisição em todo
registro novo.

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
- Teste cobre as três pontas: revendedor tentando gravar em outro, operadora
  gravando em tabela atendida, operadora barrada em tabela não atendida.
