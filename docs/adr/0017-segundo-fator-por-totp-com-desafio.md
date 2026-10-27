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

TOTP pelo `AuthenticatorTokenProvider` do Identity, opcional e só para dono e
operadora (`GestorDeSegundoFator`). Com ele ligado, `POST /auth/entrar`
responde 202 com um desafio de uso único de 5 minutos (`links_de_senha`,
finalidade `SegundoFator`, só o hash). O token só sai em
`POST /auth/entrar/codigo`, com o código do app ou um dos 10 códigos de
recuperação. Código errado conta para o bloqueio de 5 erros (15 minutos), como
a senha. A troca de senha pelo link do e-mail (`POST /auth/senha`) também cai no
desafio: o e-mail não vale pelo segundo fator.

Ligar exige o primeiro código do app. Desligar exige um código novo enviado ao
e-mail confirmado, o código do app ou um de recuperação. Ligar e desligar
avisam o e-mail do login e, no caso do dono, vão à auditoria da revenda.

## Consequências

- A chave do app e os códigos de recuperação ficam em claro em
  `usuarios_tokens`, protegidos só pelo acesso ao banco e ao backup. Na data
  desta decisão, cifrar pedia chaves do Data Protection guardadas fora do
  contêiner. A chave comum à API e ao Worker do ADR 0024 já existe e ainda não
  é usada aqui.
- Código por e-mail (22/10/2026) - alternativa ao app para quem não quer
  instalar: provedor de e-mail do Identity, só com e-mail confirmado, código
  enviado junto com o desafio e reenvio em `POST /auth/entrar/codigo/reenvio`.
  Protege menos que o app, porque quem tem a senha às vezes tem o e-mail, e a
  tela diz isso.
- Chave de acesso (WebAuthn) fica como próximo passo.
- Recuperação de conta - quem perdeu o app e os códigos pede ao suporte, que
  confere a identidade fora da plataforma e roda
  `dotnet AvaliaSales.Api.dll --desligar-segundo-fator --email=... --motivo="..."`.
  O comando desliga o segundo fator, troca a chave do app, encerra as sessões e
  avisa o e-mail do login. O motivo sai só no console, sem registro na
  auditoria.
- A auditoria é por revenda, e a operadora não tem revenda: ligar e desligar o
  segundo fator da operadora fica registrado só no e-mail de aviso.
