---
paths: ["web/src/**", "web/e2e/**"]
---

# Front

- Texto de tela pela seção Texto do `DESIGN.md`: selo e status com uma ou duas
  palavras (o teste `rotulos.test.ts` cobra o mapa de `api/rotulos.ts`), item de
  menu com uma palavra quando o grupo já dá o substantivo, botão é verbo, sem
  subtítulo explicativo nem ajuda de campo que não mude a decisão.
- Toda tabela de cadastro tem `AcoesDaLinha` com Editar à vista e Excluir no "⋯"
  (regra "Registro novo nasce com criar, editar e excluir na tela").
- Cartão mostra dado; ação do cabeçalho em `CardAction` com botão `size="sm"`.
- Mensagem de erro que repete uma da API usa o mesmo texto da API.
- Antes de entregar, o mesmo do job `web` do CI: `npm run typecheck`,
  `npm run lint`, `npm test` (o Chromium entra uma vez com `npm run navegador`)
  e `npm run build-storybook`. Com tela nova ou fluxo mudado, também
  `npm run e2e` (`CLAUDE.md`, Rodar e testar). O knip fica fora do CI:
  `npx knip@5` na limpeza (skill `codigo-morto`).
