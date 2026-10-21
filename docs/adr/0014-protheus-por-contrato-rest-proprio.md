# 0014. Protheus por contrato REST próprio, com fila

- Situação - Aceita
- Data - 21/10/2026

## Contexto

O pedido de placas pago precisa virar pedido de venda no Protheus da Avalia.
Não há ambiente público do Protheus, e as APIs dele dependem da instalação.

## Opções

- Chamar uma API padrão da TOTVS com campos supostos
- Contrato próprio, publicado por um serviço REST em ADVPL no Protheus

## Decisão

Contrato próprio (`POST pedidos-de-venda`, autenticação básica, chave do
pedido), com fila `envios_ao_erp` gravada na transação do pagamento e envio
pelo Worker. Simulador em Development.

## Consequências

- Nenhum campo da TOTVS inventado: o mapeamento para o pedido de venda fica no
  serviço ADVPL, com a equipe que mantém o Protheus.
- A chave do pedido evita pedido de venda repetido em retentativa.
- ERP fora do ar não segura o pagamento: o envio espera e a operadora vê a fila.
