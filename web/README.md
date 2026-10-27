# Front do Avalia Sales

App React único: painel do revendedor, área da operadora, app do vendedor e a
página `/now`. Como subir junto com a API, variáveis e dados de demonstração no
[README da raiz](../README.md); tokens, componentes e travas de lint no
[DESIGN.md](../DESIGN.md).

| Script | O que faz |
| --- | --- |
| `npm run dev` | Vite em `http://localhost:5173`; `/api` e `/q/` vão para a API local |
| `npm run typecheck` | `tsc -b` |
| `npm run lint` | oxlint com as travas do design system |
| `npm run navegador` | Instala o Chromium do Playwright (uma vez) |
| `npm test` | Vitest: unidade e cada história do Storybook no Chromium, com o axe |
| `npm run e2e` | Playwright com axe, contra a API local de pé |
| `npm run tipos` | Gera `src/api/esquema.d.ts` do `openapi.json` |
| `npm run storybook` | Catálogo de componentes na porta 6006 |
| `npm run build-storybook` | Compila o catálogo, como o CI |

A lista de antes de entregar está em `../.claude/rules/web.md`.
