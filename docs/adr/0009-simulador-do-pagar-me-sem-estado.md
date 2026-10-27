# 0009. Simulador do Pagar.me sem estado

Para rodar a plataforma sem conta no Pagar.me, um simulador responde no lugar
dele. Ele não guarda estado: a situação do pagamento sai da hora gravada no id
da cobrança, e a API e o Worker chegam à mesma resposta.

- Situação - Aceita
- Data - 21/10/2026
- Nota de 27/10/2026 - com o Pagar.me real em produção, o simulador fica para o
  desenvolvimento local, a demonstração (`--demonstracao`) e o teste de ponta a
  ponta do CI (`web/e2e`), que prova o Pix pago e a tela confirmando. A suíte
  da API usa um dublê próprio (`GatewayFalso`).

## Contexto

Rodar a plataforma na máquina sem conta no ambiente de testes do Pagar.me, com
API e Worker em processos separados.

## Opções

- Simulador com estado em memória - a API e o Worker teriam estados diferentes
- Simulador sem estado, decidido pelo UUID v7 da cobrança

## Decisão

`GatewaySimulado` lê a hora da criação nos 48 primeiros bits do UUID v7 da
cobrança: pendente nos primeiros 20 segundos, paga depois. Cartão aprova na
hora; token de cartão com "recusado" falha. O link do checkout abre uma página
da própria API que avisa que o pagamento é simulado. Liga com
`Pagarme:Simulado`, e a API e o Worker não sobem com ele fora de Development.

## Consequências

- O pagamento simulado passa pelo mesmo caminho da conferência de produção
  (`Retomadas` e `Cobrancas.Conferir`).
- Nenhum dado simulado sai da máquina.
- Limite - o simulador não prova o contrato do Pagar.me. Split, campos do
  pedido, o `order_code` do link e o estorno parcial só se provam no ambiente
  de testes ou em produção (pendências no SDD, seção 13).
