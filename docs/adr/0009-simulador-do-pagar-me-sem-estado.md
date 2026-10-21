# 0009. Simulador do Pagar.me sem estado

- Situação - Aceita
- Data - 21/10/2026

## Contexto

Rodar a plataforma na máquina sem conta no sandbox, com API e Worker em processos separados.

## Opções

- Simulador com estado em memória
- Simulador sem estado, decidido pelo UUID v7 da cobrança

## Decisão

`GatewaySimulado` lê a hora da criação no próprio código do pedido: pendente nos primeiros 20 segundos, paga depois. Só sobe em Development.

## Consequências

- O pagamento simulado passa pelo mesmo caminho da conferência de produção.
- Nenhum dado simulado sai da máquina.
