---
name: design-system
description: Design system do Avalia Sales, o front React único (painel do revendedor e da Avalia no computador, app do vendedor no celular) com white label por revendedor. Consultar antes de criar tela, componente, cor, fonte ou espaçamento, e ao mexer na marca do revendedor. shadcn/ui sobre Radix, Tailwind 4 com variáveis CSS, Storybook como catálogo vivo.
---

# Design system do Avalia Sales

Regra central: toda cor, fonte, raio e espaçamento sai de variável CSS.
Componente não escreve hex nem número mágico. É o que permite trocar a marca do
revendedor sem mexer em tela.

## Um front só

Um app React 19 + Vite, com React Router. As áreas são rotas do mesmo app,
liberadas pelo papel do usuário:

| Área | Quem usa | Pensada para |
| --- | --- | --- |
| Painel do revendedor | Dono | Computador, tabelas e cadastros |
| Área da Avalia | Operadora | Computador |
| Vendedor | Vendedor no balcão | Celular a 390 px, PWA com NFC e câmera |
| Página do QR | Cliente final | Celular, carrega rápido, sem login |

Um build, um conjunto de componentes, uma marca aplicada do mesmo jeito em todo
lugar.

## Cores e marca do revendedor

As cores seguem o modelo do [shadcn/ui](https://ui.shadcn.com/docs/theming):
variáveis semânticas em `:root` (`--background`, `--foreground`, `--primary`,
`--primary-foreground`, `--accent`, `--muted`, `--destructive`, `--border`,
`--ring`, `--radius` e as de gráfico), em OKLCH. O `@theme inline` do Tailwind 4
transforma cada uma em classe (`--color-primary: var(--primary)` vira
`bg-primary`).

A marca do revendedor sobrescreve só essas variáveis:

1. O dono escolhe a cor primária e a de destaque na área Marca.
2. O painel gera os tons a partir delas com
   [material-color-utilities](https://github.com/material-foundation/material-color-utilities):
   ```ts
   import { argbFromHex, themeFromSourceColor } from '@material/material-color-utilities';
   const tema = themeFromSourceColor(argbFromHex(primaria));
   ```
   e converte para os nomes de variável do shadcn.
3. Antes de salvar, confere contraste de 4,5:1 entre cada par texto e fundo
   (WCAG 2.2, luminância relativa). A API repete a conferência no Domain: a tela
   não é a única validação.
4. A API guarda as variáveis prontas no revendedor e entrega com a sessão.
5. O app aplica em `:root` logo depois do login. Nada é recompilado.

Não há cor por usuário. Login e área da Avalia usam a marca da plataforma.

As variáveis de base (marca da plataforma, escala de espaço, raio e tipo) ficam
num arquivo só, `src/estilos/tokens.css`. Se um dia outro consumidor precisar
delas, como e-mail ou PDF, elas passam para JSON no formato W3C Design Tokens e
são geradas pelo [Style Dictionary](https://github.com/style-dictionary/style-dictionary).
Até lá, um arquivo CSS basta.

## Componentes

- No modelo do [shadcn/ui](https://github.com/shadcn-ui/ui): o componente é
  copiado para `src/componentes/ui` pelo CLI e passa a ser nosso, sobre os
  primitivos acessíveis do [Radix](https://github.com/radix-ui/primitives).
  Antes de escrever um componente, ver se o shadcn já tem.
- Componente de negócio (cartão de venda, linha de placa) fica fora de `ui` e
  só compõe os de `ui`.
- [Tailwind 4](https://github.com/tailwindlabs/tailwindcss) para estilo. Classe
  com valor solto, como `bg-[#123456]` ou `p-[13px]`, não entra.
- Tabela do painel com o `DataTable` do shadcn (TanStack Table), paginação e
  ordenação no servidor.
- Formulário de cadastro com React Hook Form + Zod (`mode: 'onChange'`) e o
  componente `componentes/Campo.tsx`: erro embaixo do campo ao sair dele ou ao
  enviar, `aria-invalid`, e borda verde com o ícone de conferido quando o dado
  passa. As regras de campo (e-mail, telefone, CPF ou CNPJ, dinheiro,
  percentual, senha) moram em `lib/esquemas.ts`, iguais às da API. Filtro,
  busca e campo único dentro da linha ficam em estado simples.

## A cara da Avalia sob o white label

- O traço do site da Avalia (`--traco`, linha fina rosa > roxo > azul) é o
  único ornamento: embaixo do cabeçalho, sob o título da tela (32 px) e no
  item ativo da barra de baixo. Sempre linha, nunca fundo. A marca da revenda
  troca o traço pelo degradê destaque > primária (`marca.ts`).
- Títulos em Inter Display (opsz 32, `font-heading`), a dos títulos do site;
  corpo na Geist.
- Telas de entrada (entrar, cadastro, senha, confirmação) na noite da marca
  (`bg-noite`, #120c2b) com o cartão branco e o traço no topo: antes do login
  não há marca de revenda.

## Navegação por tamanho

| Largura | Menu |
| --- | --- |
| Celular (abaixo de 768 px) | Barra de baixo com 4 telas do papel e "Mais" em gaveta; some na Nova venda |
| Tablet (768 a 1023 px) | Menu de cima, quebrando linha |
| Computador (1024 px ou mais) | Menu lateral |

Tabela abaixo de 768 px vira cartão (`components/ui/table.tsx`): a primeira
coluna é o título, as outras levam o rótulo da coluna em cima do valor e a
coluna de ações (título só para leitor de tela) ocupa a linha toda. Tela nova
com tabela não precisa de nada a mais.

## Celular primeiro na área do vendedor

- Alvo de toque de 44 px ou mais; botão primário ao alcance do polegar, no pé
  da tela.
- Um passo por tela na Nova venda: ler a placa, achar o negócio, escolher o
  destino, cobrar.
- Leitura de QR pela câmera e NFC pelo navegador (Web NFC, só Chrome do
  Android). No iPhone, só câmera.

## Catálogo vivo e acessibilidade

- [Storybook](https://github.com/storybookjs/storybook): cada componente nasce
  com história nos estados padrão, foco, desabilitado, erro e carregando, e com
  a marca de dois revendedores diferentes lado a lado.
- Acessibilidade por [axe-core](https://github.com/dequelabs/axe-core): no addon
  de acessibilidade do Storybook e nos testes de ponta a ponta com
  `@axe-core/playwright`. Violação séria ou crítica falha o CI.
- Foco visível, `prefers-reduced-motion` respeitado, texto alternativo em
  imagem, rótulo em botão só de ícone.

## Texto de tela

Curto, para quem está no balcão: o título diz o que a tela faz, o botão é verbo
("Registrar venda", "Ativar lote") e o erro diz o que corrigir. Sem subtítulo
explicativo nem ajuda de campo que não mude a decisão.

## Como acrescentar

1. Procurar no Storybook e no shadcn: o componente quase sempre existe.
2. Cor, espaço ou raio novo entra como variável em `tokens.css`, com o papel no
   nome, nunca como valor solto no componente.
3. Componente novo nasce com história no Storybook e passa no axe.

## Bibliotecas

Conferidas na API do GitHub e no npm em 21/10/2026, pelo critério da skill
`padroes`:

| Biblioteca | Papel | Estrelas | Versão |
| --- | --- | --- | --- |
| [shadcn/ui](https://github.com/shadcn-ui/ui) | Modelo e CLI dos componentes | 126 mil | copiado |
| [Radix Primitives](https://github.com/radix-ui/primitives) | Base acessível dos componentes | 19,4 mil | |
| [Tailwind CSS](https://github.com/tailwindlabs/tailwindcss) | Estilo | 98 mil | 4.x |
| [material-color-utilities](https://github.com/material-foundation/material-color-utilities) | Tons da marca a partir de uma cor | 2,3 mil | 0.4.0 |
| [Storybook](https://github.com/storybookjs/storybook) | Catálogo vivo | 91 mil | 10.6.3 |
| [axe-core](https://github.com/dequelabs/axe-core) | Acessibilidade | 7,7 mil | `@axe-core/playwright` 4.14.0 |
| [Playwright](https://github.com/microsoft/playwright) | Teste de ponta a ponta | 98 mil | |
| [Style Dictionary](https://github.com/style-dictionary/style-dictionary) | Tokens em JSON, só se surgir outro consumidor | 4,9 mil | 5.7.0 |

Estado em 22/10/2026: traço, Inter Display, navegação por tamanho e tabela em cartão no celular; axe no Playwright (`web/e2e`) em todas as telas do dono e nos fluxos de venda e convite. Storybook ainda não instalado. Em 21/10/2026 o front tinha as telas das Fases 1 a 3 e a marca do revendedor aplicada nas variáveis (`web/src/marca`). Tokens
em `web/src/estilos/tokens.css`; toque de 44 px aplicado nos tamanhos de
`components/ui/button.tsx` e `input.tsx`; anel de foco em cor cheia. Storybook e
axe ainda não instalados (pendência no SDD).
