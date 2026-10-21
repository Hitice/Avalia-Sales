# 0007. Código da placa em tabela da plataforma

- Situação - Aceita
- Data - 21/10/2026

## Contexto

A leitura do QR chega sem login e precisa descobrir de quem é a placa; antes do despacho, a placa é estoque da Avalia e não pertence a revendedor.

## Opções

- Placa inteira numa tabela sem RLS
- Código e dono em `placas_emitidas`, sem RLS; destino, venda e situação em `placas`, com RLS

## Decisão

`placas_emitidas` guarda só identificadores (código, sequência, lote, revendedor). A leitura descobre o revendedor por ela, abre um escopo novo, assume o revendedor e lê a placa com RLS.

## Consequências

- Destino e venda continuam isolados por revendedor.
- O teste de RLS lista `lotes` e `placas_emitidas` como exceção justificada.
