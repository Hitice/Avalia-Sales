# 0021. Limite e conferência do recebido por fora guardados na auditoria

- Situação - Aceita
- Data - 25/10/2026

## Contexto

O vendedor registra o dinheiro ou o Pix recebido por fora da plataforma, e isso
gera comissão devida sem prova. O balcão depende desse registro, então a
permissão fica. A garantia precisa vir de um teto por vendedor e de uma
conferência do revendedor antes de a comissão entrar num repasse.

## Opções

- Colunas dedicadas em `vendedores` (limites) e `cobrancas` (conferência)
- Guardar o limite vigente e a conferência na tabela `auditoria`, que já é só de inserção

## Decisão

A segunda. O limite vigente é o último registro `LimiteDePorForaDefinido` do
vendedor (JSON em `Depois`), e a conferência é um registro `RecebimentoConferido`
com a cobrança como referência. O código fica isolado em `RecebimentosPorFora`.

- Sem registro, vale o padrão - R$ 10.000,00 e 30 recebimentos por dia, e
  R$ 5.000,00 por venda.
- O dono não tem limite, e o que ele registra já nasce conferido.
- A comissão de venda ainda não conferida fica fora do repasse (`Comissoes`).

## Consequências

- Nenhuma migration, e cada ajuste de limite e cada conferência deixa rastro de
  quem fez e quando.
- Ler o limite e as pendências é uma consulta à auditoria por ação e vendedor; se
  o volume crescer, a troca por colunas (`vendedores.limite_por_fora_dia_cents`,
  `limite_por_fora_dia_qtd`, `limite_por_fora_venda_cents`, `cobrancas.conferida_em`,
  `conferida_por`) é local a essa classe.
- Recebimentos por fora de vendedor anteriores a esta decisão e ainda sem repasse
  aparecem como pendentes até o revendedor usar "Conferir todos".
