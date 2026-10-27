# 0011. Razão só de inserção, e o saldo de comissão lido dele

O financeiro da revenda é um razão em que só se insere linha. Correção é
estorno: uma linha com o valor contrário, que aponta para a original. O saldo
de comissão de cada vendedor é lido do razão.

- Situação - Aceita
- Data - 21/10/2026

## Contexto

O revendedor precisa saber quanto recebeu, quanto ficou com a Avalia e quanto
deve aos vendedores, mês a mês, e o número de um mês fechado não pode mudar
depois que a comissão foi paga.

## Opções

- Somar na hora a partir de vendas e cobranças - uma venda corrigida mudaria
  um mês já fechado
- Razão só de inserção, com estorno como linha contrária

## Decisão

Tabela `lancamentos` só de inserção. Desde 24/10/2026 o banco também garante
isso: o papel da aplicação não tem `UPDATE` nem `DELETE` nela.

- Venda paga lança a receita, a parte da Avalia nos serviços (tipo
  `TaxaAvalia`, ADR 0003) e a comissão devida.
- Repasse ao vendedor lança a comissão paga.
- Estorno lança a linha contrária na competência do dia, apontando para a
  original (`estorna_id`).

Índices únicos impedem lançar o mesmo fato duas vezes e estornar a mesma linha
duas vezes. O saldo de comissão é a comissão devida menos a paga, lido do
razão.

## Consequências

- Estorno depois do repasse vira saldo negativo, descontado no próximo repasse.
- Correção nunca apaga: o histórico explica qualquer número antigo.
- `PermissoesDoBancoTest` confere que a aplicação não altera nem apaga linha do
  razão.
- Mês fechado não recebe linha nova (ADR 0020).
