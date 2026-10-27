# 0006. Refresh token rotativo em cookie HttpOnly

O refresh token fica num cookie `HttpOnly`, que o JavaScript da página não lê,
e mantém a sessão depois de recarregar a página. Cada renovação troca o token;
o token antigo apresentado de novo encerra a sessão daquele login.

- Situação - Aceita
- Data - 21/10/2026

## Contexto

O token de acesso fica só em memória no front, longe de script injetado. Sem
mais nada, recarregar a página derruba a sessão.

## Opções

- Token em `localStorage` - qualquer script da página lê
- Refresh em cookie HttpOnly, rotativo

## Decisão

Refresh de 14 dias (`Sessao.Validade`) em cookie HttpOnly, SameSite Strict e
Secure fora de Development, guardado no banco só pelo hash SHA-256
(`sessoes.hash_do_token`). Cada renovação troca o token. O token já trocado,
apresentado de novo, encerra a família inteira daquele login (RFC 9700, seção
4.14).

Exceção - até 10 segundos depois da troca, e no máximo 10 vezes por token, o
token anterior ainda renova. Esse caso é duas abas renovando juntas ou uma
resposta perdida, e não roubo.

## Consequências

- Script não lê o refresh. O token de acesso, de 15 minutos
  (`Jwt:MinutosDeValidade`), continua só em memória.
- Sair, trocar a senha ou ter o login desativado encerra as sessões abertas. O
  token de acesso já emitido vale até expirar, em no máximo 15 minutos.
