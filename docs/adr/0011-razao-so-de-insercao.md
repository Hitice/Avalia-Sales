# 0011. Razão só de inserção, e o saldo de comissão lido dele

- Situação - Aceita
- Data - 21/10/2026

## Contexto

O revendedor precisa saber quanto recebeu, quanto ficou com a Avalia e quanto
deve aos vendedores, mês a mês, e o número de um mês fechado não pode mudar
depois que a comissão foi paga.

## Opções

- Somar na hora a partir de vendas e cobranças
- Razão só de inserção, com estorno como linha contrária

## Decisão

Tabela `lancamentos` só de inserção. Venda paga lança receita, taxa e comissão
devida; repasse lança comissão paga; estorno lança a linha contrária na
competência do dia, apontando para a original. Índices únicos impedem lançar o
mesmo pagamento duas vezes e estornar a mesma linha duas vezes. O saldo de
comissão é a comissão devida menos a paga, lida do razão.

## Consequências

- Estorno depois do repasse vira saldo negativo, descontado no próximo repasse.
- Correção nunca apaga: o histórico explica qualquer número antigo.
