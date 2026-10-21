# 0006. Refresh token rotativo em cookie HttpOnly

- Situação - Aceita
- Data - 21/10/2026

## Contexto

O token de acesso fica só em memória no front, longe de script injetado. Sem mais nada, recarregar a página derruba a sessão.

## Opções

- Token em `localStorage`
- Refresh em cookie HttpOnly, rotativo

## Decisão

Refresh de 14 dias em cookie HttpOnly, SameSite Strict e Secure fora de Development, guardado no banco só pelo hash. Cada renovação troca o token; o token já trocado apresentado de novo encerra a família inteira daquele login (RFC 9700, seção 4.14).

## Consequências

- Script não lê o refresh; o acesso de 15 minutos continua só em memória.
- Login desativado perde as sessões abertas.
