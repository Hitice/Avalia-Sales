# 0012. Pedido de placas pelo checkout do Pagar.me, sem split

A revenda paga o pedido de placas à Avalia por um link do checkout do
Pagar.me, sem split. Na página do Pagar.me ela escolhe Pix, cartão ou boleto, e
a plataforma não toca dado de cartão.

- Situação - Aceita, revista em 27/10/2026
- Data - 21/10/2026
- Revista - 27/10/2026, texto alinhado ao código: cancelar o pedido cancela o
  link em aberto, e a "taxa de 100%" passou a ser a parte da Avalia igual ao
  valor da cobrança (ADR 0003)

## Contexto

O revendedor paga o pacote de placas à Avalia antes do envio. A venda ao
cliente final já usa `POST /orders` com split. O pedido de placas não tem
split, e o revendedor paga no computador, sem pressa de balcão.

## Opções

- `POST /orders` com Pix gerado pela plataforma
- Link do checkout do Pagar.me (`POST /paymentlinks`)

## Decisão

Link do checkout (`POST /paymentlinks`, `type` order), com `order_code` igual
ao código da cobrança, um pagamento só (`max_paid_sessions` 1) e validade de 3
dias. A cobrança guarda o valor inteiro como parte da Avalia
(`ParteDaAvaliaCents` igual ao valor).

## Consequências

- O pedido no Pagar.me só existe depois do pagamento. A conferência procura
  pelo código, e o link vencido sem pagamento cancela a cobrança. Pagar de novo
  devolve o link em aberto, ou cria outro se o anterior venceu ou falhou.
- Cancelar o pedido cancela o link em aberto no Pagar.me. Pagamento que chega
  para pedido já pago, pago por fora ou cancelado avisa a operadora por e-mail
  para devolver.
- A operadora também registra pagamento recebido por fora, com segundo fator
  (ADR 0027).
- Pendente de prova - se o pedido gerado pelo link leva o `order_code` no
  `code`. Sem isso, nem o webhook nem a conferência acham o pagamento, e a
  cobrança vence como cancelada com o dinheiro recebido. Conferir no ambiente
  de testes ou no primeiro pagamento real (SDD, seção 13).
