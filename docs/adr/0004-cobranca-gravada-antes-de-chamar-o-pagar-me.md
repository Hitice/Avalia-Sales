# 0004. Cobrança gravada antes de chamar o Pagar.me

- Situação - Aceita
- Data - 21/10/2026

## Contexto

A referência do Pagar.me não documenta cabeçalho de idempotência. Uma chamada que cai no meio pode ter criado o pedido.

## Opções

- Repetir o POST
- Gravar a cobrança antes, com o próprio id como `code` do pedido, e procurar pelo código antes de qualquer nova tentativa

## Decisão

A cobrança nasce pendente no banco com o vínculo; o `code` do pedido é o id dela. A retomada procura `GET /orders?code=`; sem pedido depois de 15 minutos, a cobrança falhou e pode ser refeita. A resiliência só repete método seguro.

## Consequências

- Nenhuma cobrança em dobro por queda de rede.
- O pagamento da venda é lido da cobrança por origem, então a cobrança órfã já aponta para a venda.
