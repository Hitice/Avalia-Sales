# 0004. Cobrança gravada antes de chamar o Pagar.me

A cobrança é gravada antes da chamada ao Pagar.me, e o id dela vai como código
do pedido. Se a chamada cair, o sistema procura o pedido pelo código antes de
qualquer nova tentativa; sem pedido depois de 15 minutos, a cobrança é marcada
como falha e pode ser refeita. Assim, queda de rede não gera cobrança em dobro.

- Situação - Aceita
- Data - 21/10/2026
- Nota de 27/10/2026 - contexto corrigido: o Pagar.me aceita o cabeçalho
  `Idempotency-key` na criação de pedido. A decisão continua a mesma.

## Contexto

Uma chamada que cai no meio pode ter criado o pedido no Pagar.me sem a
plataforma saber. O Pagar.me aceita o cabeçalho `Idempotency-key` na criação de
pedido, mas a chave vale 24 horas (5 minutos no ambiente de testes) e só evita o
pedido repetido: quando a resposta se perde, ela não diz se o pedido existe.

## Opções

- Repetir o POST
- Repetir o POST com `Idempotency-key`
- Gravar a cobrança antes, com o próprio id como `code` do pedido, e procurar
  pelo código antes de qualquer nova tentativa

## Decisão

A cobrança nasce pendente no banco, junto com o vínculo (`vinculos_externos`),
na mesma transação. O `code` do pedido é o id dela sem hífens
(`Cobranca.Codigo`). A rotina do Worker (`Retomadas`) pega a cobrança pendente
sem pedido, passados 2 minutos, e procura `GET /orders?code=`: achou, liga o
pedido à cobrança; sem pedido depois de 15 minutos, a cobrança falha e pode ser
refeita. A retentativa automática do cliente HTTP só repete método seguro
(GET); POST nunca é repetido por ela.

No link do checkout (ADR 0012), o pedido só nasce quando a revenda paga: falha
ao criar o link marca a cobrança como falha na hora, porque link não criado não
cobra ninguém.

## Consequências

- Queda de rede não gera cobrança em dobro.
- O pagamento da venda é lido da cobrança pela origem, então a cobrança que
  ficou sem pedido já aponta para a venda.
- Duas cobranças da mesma venda ao mesmo tempo (dois toques, duas abas) passam
  por uma trava por origem (`TravarOrigemDaCobranca`), e a segunda encontra a
  primeira.
