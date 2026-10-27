# 0022. Cadastro pendente por link, sem senha no cadastro

- Situação - Aceita
- Data - 25/10/2026

## Contexto

O cadastro público criava a revenda e o dono com a senha digitada e já devolvia o
token. Quem cadastrasse o e-mail de outra pessoa reservava o endereço, e a resposta
revelava se o e-mail ou o CNPJ já tinham conta. Também mandava e-mail para qualquer
endereço, limitado só por IP.

## Opções

- Manter o fluxo e confirmar o e-mail depois, com a conta já funcionando
- Cadastro pendente sem senha: a conta só existe de fato depois do link do e-mail,
  que abre a tela de criar a senha
- Guardar o cadastro fora de `revendedores` até a confirmação

## Decisão

A segunda. O cadastro cria revenda e dono sem senha, com `EmailConfirmed = false` e
um link `CadastroPendente` em `links_de_senha`; o marcador é um valor novo do campo
de texto, sem migration. Pendente não entra, não recupera senha e expira em 48 horas,
quando o Worker libera o e-mail e o CNPJ. A resposta é a mesma (202) para e-mail e
CNPJ novos ou já cadastrados, e quem já tem conta recebe o aviso por e-mail.

Contra abuso:

- Captcha Turnstile - desligado enquanto `Turnstile__ChaveSecreta` estiver vazia.
- Limites - por IP (limitador da rota), por CNPJ (5 por hora), por endereço de
  e-mail (contado na tabela `emails`) e um teto diário de e-mails do cadastro
  para todos (`Cadastro:EmailsPorDia`, 300). Com o teto atingido, a resposta é
  "Muitos cadastros no momento" para qualquer pedido.

## Consequências

- Senha digitada por quem não controla o e-mail deixa de existir, e o cadastro não
  calcula hash de senha, o que aproxima o tempo de resposta entre os dois casos.
- Quem controla uma caixa de e-mail ainda descobre que um CNPJ existe, ao custo de um
  e-mail do teto diário. Eliminar isso pede guardar o cadastro fora de `revendedores`.
- Repetir o e-mail ou o CNPJ de um pendente o substitui, porque o pendente não tem dono
  provado; os limites contêm o abuso.
- Donos anteriores a esta regra continuam entrando, inclusive os que nunca confirmaram
  o e-mail, no fluxo de confirmação que já existia.
- Os limites por IP e por CNPJ ficam na memória da API e zeram quando ela reinicia;
  o limite por endereço e o teto diário vêm da tabela `emails` e não zeram.
