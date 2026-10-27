# 0014. Protheus por contrato REST próprio, com fila

O pedido de placas pago vira pedido de venda no Protheus da Avalia por um
contrato REST definido pela plataforma. O envio passa por uma fila, então o
Protheus fora do ar não segura o pagamento.

- Situação - Aceita
- Data - 21/10/2026
- Nota de 27/10/2026 - sem `Protheus:UrlBase` a integração fica desligada:
  `FilaDoErp` não grava em `envios_ao_erp` e a tela ERP avisa. O simulador
  segue só em Development, e os testes usam um dublê (`ErpFalso`).

## Contexto

O pedido de placas pago precisa virar pedido de venda no Protheus da Avalia.
Não há ambiente público do Protheus, e as APIs dele dependem da instalação.

## Opções

- Chamar uma API padrão da TOTVS com campos supostos
- Contrato próprio, publicado por um serviço REST em ADVPL no Protheus

## Decisão

Contrato próprio: `POST pedidos-de-venda`, autenticação básica e a chave do
pedido (o id do pedido de placas). A fila `envios_ao_erp` é gravada na
transação do pagamento, e o Worker (`EnvioAoErpRotina`) envia, com até 8
tentativas. A operadora vê a fila e reenvia. Simulador em Development.

## Consequências

- Nenhum campo da TOTVS inventado: o mapeamento para o pedido de venda fica no
  serviço ADVPL, com a equipe que mantém o Protheus.
- A chave do pedido evita pedido de venda repetido em retentativa, desde que o
  serviço ADVPL devolva o mesmo número para a mesma chave. Isso faz parte do
  contrato e precisa ser provado com a equipe do Protheus.
- ERP fora do ar não segura o pagamento: o envio espera, e a operadora vê a
  fila.
- Pedido pago com a integração desligada não entra na fila, nem depois de
  ligada.
- Pedido estornado depois do envio é cancelado à mão no Protheus; o e-mail do
  estorno à operadora lembra disso.
