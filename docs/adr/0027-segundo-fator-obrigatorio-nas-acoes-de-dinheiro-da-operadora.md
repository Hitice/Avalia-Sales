# 0027. Segundo fator obrigatório nas ações de dinheiro da operadora

- Situação - Aceita
- Data - 26/10/2026

## Contexto

A operadora é uma conta só. Ela troca o recebedor do split de qualquer revenda,
registra pagamento por fora de pedido (o que libera estoque), estorna pedido e lança
acerto. Com uma conta só, não há segunda pessoa para aprovar, e a senha roubada
bastaria para redirecionar o dinheiro de uma revenda. O segundo fator (ADR 0017) é
opcional.

## Opções

- Dupla aprovação por outra pessoa
- Segundo fator obrigatório na conta da operadora para entrar
- Segundo fator exigido só nas ações de dinheiro, mais aviso ao dono da revenda

## Decisão

A terceira. `AcoesSensiveisDaOperadora.Exigir` recusa a ação quando o login não tem o
segundo fator ligado, com a mensagem para ligar em Segurança. As ações são trocar
nome e CNPJ da revenda, trocar o recebedor, registrar pagamento por fora, estornar
pedido e registrar acerto. A troca de recebedor avisa o dono no sino e por e-mail.
A exigência é a opção `Plataforma:ExigirSegundoFatorDaOperadora`, ligada por padrão;
só o desenvolvimento local e os testes a desligam.

## Consequências

- A operadora segue entrando sem segundo fator e não perde o acesso; só não faz
  essas ações até ligá-lo.
- Quem roubar a senha da operadora não troca o recebedor sem o segundo fator, e o dono
  da revenda é avisado de toda troca.
- Quando houver uma segunda pessoa na operadora, a dupla aprovação passa a ser
  possível e esta decisão vira o mínimo.
