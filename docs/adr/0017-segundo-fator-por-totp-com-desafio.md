# 0017. Segundo fator por app autenticador, com desafio de uso único

- Situação - Aceita
- Data - 22/10/2026

## Contexto

Dono e operadora mexem em dinheiro (repasse, comissão, recebido por fora) e em
dados de cliente. Senha vazada não pode bastar. O app é web, sem SMS
contratado, e o vendedor no balcão não deve ganhar um passo a mais.

## Opções

- Código por e-mail a cada entrada
- App autenticador (TOTP, RFC 6238) pelo provedor do ASP.NET Identity
- Chave de acesso (WebAuthn)

## Decisão

TOTP pelo `AuthenticatorTokenProvider` do Identity, opcional, para dono e
operadora. Com ele ligado, `/auth/entrar` responde 202 com um desafio de uso
único de 5 minutos (`links_de_senha`, só o hash), e o token só sai em
`/auth/entrar/codigo` com o código do app ou um dos 10 de recuperação. Código
errado conta para o bloqueio de 5 erros. A troca de senha pelo link do e-mail
também cai no desafio: o e-mail não vale pelo segundo fator. Ligar exige o
primeiro código do app; desligar, um código válido. Ligar e desligar vão à
auditoria.

## Consequências

- A chave do app e os códigos de recuperação ficam em `usuarios_tokens`,
  protegidos pelo acesso ao banco; criptografar com Data Protection pede chaves
  persistidas fora do contêiner, e fica para quando houver.
- Em 22/10/2026 entrou o código por e-mail como alternativa ao app, a pedido,
  para quem não quer instalar app: provedor de e-mail do Identity (TOTP sobre a
  marca de segurança), só com e-mail confirmado, código enviado junto com o
  desafio e reenvio na mesma tela. Protege menos que o app, e a tela diz isso.
  WebAuthn fica como próximo passo.
