# 0003. Split do Pagar.me em centavos (`flat`)

- Situação - Aceita
- Data - 21/10/2026

## Contexto

A taxa da Avalia é 1,99% de cada venda paga. O tipo `percentage` do split não aceita casa decimal.

## Opções

- `percentage` arredondado para 2%
- `flat` calculado pela plataforma

## Decisão

`flat`: a divisão é calculada em centavos, arredondada ao centavo mais próximo, e o resto fica com o revendedor, que assume taxa, chargeback e resto do arredondamento.

## Consequências

- A soma sempre fecha com o valor cobrado.
- A taxa fica gravada na cobrança no momento dela.
