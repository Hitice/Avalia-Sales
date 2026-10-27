# 0003. Split do Pagar.me em centavos (`flat`)

O split do Pagar.me divide cada cobrança em valores fixos, em centavos,
calculados pela plataforma: a parte da Avalia nos serviços vai para ela, e o
resto vai para a revenda. A soma das partes sempre fecha com o valor cobrado.

- Situação - Aceita, revista em 27/10/2026
- Data - 21/10/2026

## Contexto

O split do Pagar.me manda à Avalia o que é dela em cada cobrança e o resto à
revenda. Na decisão original, o que era da Avalia incluía uma taxa de 1,99% de
cada venda paga, e o tipo `percentage` do split não aceita casa decimal.

Revisão de 27/10/2026 - a taxa percentual da plataforma deixou de existir: a
Avalia ganha na venda dos produtos, no pedido de placas e na parte dela nos
serviços. O que vai à Avalia pelo split passa a ser só a parte dela no
serviço, um valor em centavos gravado na venda (o preço sugerido menos o
desconto da revenda, já sem a fatia dela na tarifa no Pix e no cartão à
vista). O pedido de placas sai pelo checkout, sem split, e a cobrança dele
guarda o valor inteiro como da Avalia (ADR 0012).

## Opções

- `percentage` arredondado - não representa um valor em centavos, e a soma das
  regras não fecha com o valor cobrado
- `flat` calculado pela plataforma

## Decisão

`flat`: a divisão é calculada em centavos pela plataforma, e o resto fica com a
revenda, que assume a tarifa, o chargeback e o resto do arredondamento. A
cobrança guarda o que vai à Avalia (`Cobranca.ParteDaAvaliaCents`, coluna
`taxa_avalia_cents`). Venda de placa e renovação não mandam nada à Avalia: o
split vai numa regra só, inteira para a revenda.

## Consequências

- A soma sempre fecha com o valor cobrado.
- A parte da Avalia fica gravada na cobrança no momento dela.
- Na cobrança de um grupo de vendas, a parte é rateada só entre as vendas de
  serviço; a venda de placa do grupo não recebe linha da Avalia no razão.
- `revendedores.taxa_avalia_bps` não é lida nem gravada desde 27/10/2026 e sai
  numa migration posterior. O nome da coluna `cobrancas.taxa_avalia_cents` e
  do tipo `TaxaAvalia` do razão vêm da taxa e ficam, para não mudar dado
  gravado; o tipo `TaxaDaPlataforma` do livro da operadora fica só para as
  linhas antigas, que somam na parte da Avalia no DRE, no razão e no
  resultado do grupo.
