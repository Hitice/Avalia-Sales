# 0024. E-mail sensível cifrado em repouso

- Situação - Aceita
- Data - 26/10/2026

## Contexto

O e-mail com link de senha ou código de acesso ficava em claro na fila (`emails`) até o
envio, e o texto só era apagado depois. Com o Resend fora do ar, isso dura até 2 dias. A
API grava e o Worker envia, em contêineres separados que não compartilham chaves do
ASP.NET Data Protection.

## Opções

- `IDataProtector` com as chaves guardadas no banco (ou em volume compartilhado)
- AES-GCM do .NET com uma chave de configuração igual na API e no Worker
- Não gravar o conteúdo e remontá-lo no Worker a partir do link

## Decisão

A segunda: `Seguranca__ChaveDosEmails`, 32 bytes em base64, no `.env` e entregue aos dois
contêineres. É a de menor operação: nada novo no banco, nenhum volume, nenhuma dependência
além do `System.Security.Cryptography`. Assunto e texto viram um par JSON cifrado por AES-256-GCM
com nonce aleatório por linha, no formato `avalia-cifra:v1:` + base64(nonce, tag, cifra), gravado
na coluna `texto`; o assunto fica `(conteúdo protegido)`. A chave única do e-mail entra como dado
associado, e uma linha copiada para outro e-mail não abre. Sem migration. A linha antiga em claro
(sem o prefixo) ainda envia. A rotação usa `Seguranca__ChaveAnteriorDosEmails` por 2 dias, a maior
validade de um link. Fora do Development, a API e o Worker não sobem sem a chave.

`IDataProtector` com chaves no banco foi descartado: as chaves ficariam no mesmo banco que as
linhas cifradas, e o ganho seria só a rotação automática, que o par de chaves já cobre.

## Consequências

- Quem lê só o banco ou o backup não vê link nem código dos e-mails ainda na fila. Quem tem o
  banco e o `.env` vê.
- A chave perdida ou diferente entre API e Worker faz o e-mail cifrado virar falha definitiva,
  com o conteúdo apagado; o usuário pede o link ou o código de novo.
- O e-mail não sensível segue em claro.
