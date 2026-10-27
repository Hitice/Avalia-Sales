# 0007. Código da placa em tabela da plataforma

A leitura do QR chega sem login. Para descobrir de quem é a placa, o código e o
dono ficam numa tabela da plataforma, sem RLS. O resto da placa (destino, venda,
situação) continua isolado por revendedor.

- Situação - Aceita
- Data - 21/10/2026

## Contexto

A leitura do QR chega sem login e precisa descobrir de quem é a placa. Antes da
ativação do lote, a placa é estoque da Avalia e não pertence a revendedor.

## Opções

- Placa inteira numa tabela sem RLS - destino e venda de todos os revendedores
  ficariam abertos a qualquer consulta
- Código e dono em `placas_emitidas`, sem RLS; destino, venda e situação em
  `placas`, com RLS

## Decisão

`placas_emitidas` guarda só identificadores: código, sequência, lote e
revendedor, preenchido quando a revenda ativa o lote. A leitura de
`/q/{codigo}` descobre o revendedor por ela, abre um escopo novo, assume o
revendedor (`AcessoDaRequisicao.Assumir`) e lê a placa em `placas`, com RLS.
Código ainda sem revendedor responde que a placa não foi ativada.

## Consequências

- Destino, venda e situação continuam isolados por revendedor.
- O teste de RLS lista `lotes` e `placas_emitidas` como exceção justificada.
- O link curto `/l/{codigo}` segue o mesmo desenho, com `codigos_de_link`.
- O código de lote excluído antes do primeiro download vai para
  `codigos_reservados` e nunca é sorteado de novo.
