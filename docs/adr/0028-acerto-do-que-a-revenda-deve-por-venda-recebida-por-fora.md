# 0028. Acerto do que a revenda deve por venda recebida por fora

- Situação - Aceita
- Data - 26/10/2026

## Contexto

A venda recebida por fora (dinheiro ou outro Pix) não passa pelo split do Pagar.me,
então a taxa da plataforma e a parte da Avalia nos serviços ficam "a acertar" no livro
da operadora (ADR 0025). A parte do serviço é a maior receita da Avalia e não tinha
caminho para ser cobrada: só o distrato levava o saldo, e só no fim da relação.

## Opções

- Proibir o recebimento por fora de serviço
- Lançar a quitação como linha do livro da operadora
- Registro próprio de quitação, fora do livro

## Decisão

A terceira. O devido de cada revenda é a soma das linhas do livro marcadas `por_fora`
(já líquidas de estorno), e a quitação é uma linha em `acertos_de_revenda`, lançada só
pela operadora. A receita já está no livro desde a venda: a quitação é caixa, não
resultado, então não entra no livro. A revenda lê os acertos dela, para o distrato
descontar o que já quitou. O acerto não passa do saldo devido.

## Consequências

- O recebimento por fora de serviço continua permitido, e o balcão não muda.
- A operadora vê em Resultado do grupo quanto cada revenda deve e quanto já quitou.
- O saldo devido não tem prazo nem juros no sistema: a cobrança em si acontece fora dele.
