# 0012. Pedido de placas pelo checkout do Pagar.me, sem split

- Situação - Aceita
- Data - 21/10/2026

## Contexto

O revendedor paga o pacote de placas à Avalia antes do envio. A venda ao
cliente final já usa `POST /orders` com split; o pedido de placas não tem
split, e o revendedor paga no computador, sem pressa de balcão.

## Opções

- `POST /orders` com Pix gerado pela plataforma
- Link do checkout do Pagar.me (`POST /paymentlinks`)

## Decisão

Link do checkout, com `order_code` igual ao id da cobrança, um pagamento só e
validade de 3 dias. A cobrança tem taxa de 100% (todo o valor é da Avalia). O
link aceita Pix, cartão e boleto sem a plataforma tocar dado de cartão.

## Consequências

- O pedido no Pagar.me só existe depois do pagamento; a conferência procura
  pelo código, e o link vencido cancela a cobrança.
- Pedido com link em aberto não é cancelado. Pagamento que chega para pedido
  já pago ou cancelado avisa a operadora para devolver.
- A conferir no sandbox: se o pedido do link leva o `order_code` no `code`.
