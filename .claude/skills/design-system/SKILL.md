---
name: design-system
description: Design system do Avalia Sales, o front React único (painel do revendedor e da Avalia no computador, app do vendedor no celular) com white label por revendedor. Consultar antes de criar tela, componente, cor, fonte ou espaçamento, e ao mexer na marca do revendedor. shadcn/ui sobre Radix, Tailwind 4 com variáveis CSS, Storybook como catálogo vivo.
---

# Design system do Avalia Sales

O sistema publicado, com os valores, está em `DESIGN.md` na raiz: ler antes de
criar tela ou componente. Direção escolhida pelo dono em 24/10/2026 - o padrão
da categoria (Stripe Dashboard, Linear, Shopify Admin, apps PJ de banco),
executado sem defeito; proibido o visual de template de IA e o que quebre a
marca da revenda (`PRODUCT.md`).

Regra central: toda cor, fonte, raio e espaçamento sai de variável CSS.
Componente não escreve hex nem número mágico. É o que permite trocar a marca do
revendedor sem mexer em tela. O lint cobra (`npm run lint`, seção Travas do
`DESIGN.md`).

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

A marca do revendedor sobrescreve só as variáveis de marca:

1. O dono escolhe a cor primária e a de destaque na área Marca.
2. A tela e a API (`Contraste.cs` no Domain) recusam primária sem 4,5:1 com o
   texto por cima e destaque com menos de 3:1 sobre branco (WCAG 2.2,
   luminância relativa). A tela não é a única validação.
3. A API guarda as cores e o texto sobre a primária e entrega com a sessão.
4. `variaveisDaMarca` (`web/src/marca/marca.ts`, com o culori em OKLCH) gera
   `--primary` (a cor inteira), `--primary-foreground` (o texto gravado),
   `--primary-texto` (a primária escurecida até 4,5:1 sobre branco, para link
   e ícone ativo), `--chart-1` (escurecida até 3:1, para a série principal),
   `--ring` e `--destaque` (o destaque, no foco) e `--chart-2` (cinza quando a
   primária é quase preta). `--primary-hover` segue a primária sozinho.
5. `aplicarMarca` grava em `:root` logo depois do login. Nada é recompilado.

Não há cor por usuário. Login e área da Avalia usam a marca da plataforma.

A cor da revenda aparece só na ação principal, no ícone do item ativo, na
seleção de texto (a primária a 20%), no foco (contorno sólido de 2 px no
`--ring`) e no dado principal do gráfico. O item ativo fica em fundo neutro
(`--accent` é o cinza-100). Estado nunca usa a cor da marca: vai em selo com
tom semântico (seção Selos). Modal abre com o foco nele mesmo, sem contorno no
primeiro botão.

Cabeçalho: logo (ou nome) à esquerda e, à direita, o sino de notificações
(dono e operadora, com o número de não lidas) e o menu da conta (iniciais
num círculo, nome a partir de `md`) com Meus dados, Segurança e Sair. O nome
lido do botão é "Conta de Fulano", com o nome visível dentro (WCAG 2.5.3).
No computador, o logo fica na célula do topo do painel lateral (mesma largura
e borda) e o menu da conta passa para o pé do painel; o cabeçalho fica só com
o sino.

Gráfico: série principal em `--chart-1`, nunca a primária crua, barras finas com
ponta de 2 px, dia zerado num traço cinza de 2 px, grade só horizontal, tooltip ao
passar e a mesma informação numa tabela `sr-only`. Rosca (`Rosca` em
`financeiro/Graficos.tsx`) com até quatro partes; com `primeira`, essa parte fica
sempre na cor da marca. Antes de mudar a cor, rodar o validador da skill dataviz.

Carregando: barra no topo (`barra-de-carregando`, aparece depois de 300 ms)
enquanto a tela baixa ou uma consulta roda; dentro da tela, `Carregando`
(linhas de 44 px pulsando) no lugar da lista, só onde a espera é real, e
`CarregandoTela` quando nada aparece sem o dado. Lista vazia - `Vazio` (ou
`EstadoDaConsulta` com `acao`): uma linha e, quando houver, uma ação. Logo da revenda passa pela caixa de recorte
(`RecorteDoLogo`, `react-easy-crop`), horizontal 3:1 ou quadrado, começando
com a imagem inteira enquadrada.

Logo da plataforma em `web/public/marca`, em SVG e com o texto em curvas:
`avaliasales.svg` (gauge e "AvaliaSales") nas telas de entrada (entrar,
cadastro, senha) e no topo do painel lateral de quem não tem logo nem nome
próprio, pelo `MarcaAvalia`; `gauge.svg` no cabeçalho do celular e do tablet
de quem não tem logo próprio; `favicon.svg` é o gauge com o ponteiro claro em aba escura. O
"Avalia" e o gauge vêm do logo da Avalia; o "Sales" é Ubuntu Medium na escala do
"One" original. Revenda com logo usa o dela no cabeçalho.

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
  passa (`data-valido`; a classe `contorno-de-campo` do `index.css` põe a borda e
  o halo de 3 px no `--ring` no foco e mantém o verde e o vermelho por cima dele). Documento,
  telefone e código usam `mascara` (`lib/mascaras.ts`): só dígitos, com a
  pontuação, e a API recebe só os dígitos. As regras de campo (e-mail, telefone, CPF ou CNPJ, dinheiro,
  percentual, senha) moram em `lib/esquemas.ts`, iguais às da API. Endereço usa `componentes/CamposDeEndereco.tsx` (o CEP completo busca rua, bairro, cidade e UF; a consulta só preenche campo vazio). Filtro,
  busca e campo único dentro da linha ficam em estado simples.

## Selos

Situação vai no `Badge` com tom semântico: `sucesso` (paga, entregue, ativa),
`atencao` (aguardando, carência, a fazer), `erro` (falhou, cancelada,
vencida), `info` (anda sozinho: pago, em produção, enviado) e `neutro` (o
resto), cada um com ponto na cor do texto. O tom sai dos mapas de
`api/rotulos.ts` (`TOM_DO_ESTADO`, `TOM_DO_PEDIDO`, `TOM_DA_ORDEM`,
`TOM_DO_PRODUTO`, `TOM_DO_PAGAMENTO`, `TOM_DO_CONTRATO`) e, na venda, de
`painel/situacaoDaVenda.ts`. `default` (cor da marca) é só contagem.

## Tipo e movimento

- Uma face, Geist, com número tabular em tudo; pesos 400 e 500. Título da
  tela em 20 px, indicador em 24 px, corpo em 13 px, cabeçalho de tabela e
  selo em 12 px, eixo e menu em 11 px. A escala padrão do Tailwind está
  desligada: só as classes de `index.css` existem.
- Movimento de 150 a 200 ms com saída exponencial (`ease-saida`), CSS primeiro
  (`transition-colors`, `starting:` para entrada). `<ViewTransition>` do React
  dentro de `startTransition` só na troca de etapa da Nova venda e na
  confirmação do Pix; o QR do Pix nunca espera transição. Tudo atrás de
  `prefers-reduced-motion`.
- Telas de entrada (entrar, cadastro, senha, confirmação) no fundo claro, com
  o cartão e o logo da plataforma: antes do login não há marca de revenda.

## Navegação por tamanho

| Largura | Menu |
| --- | --- |
| Celular (abaixo de 768 px) | Barra de baixo com 4 telas do papel (Início, Vender, Vendas e Estoque ou Placas; o Catálogo é a aba Catálogo de Vendas) e "Mais" em folha que sobe de baixo, ícone `size-5` (o ativo na cor da marca) e altura `min-h-13`; na Nova venda o botão da venda fica fixo acima dela |
| Tablet (768 a 1023 px) | Menu de cima, quebrando linha |
| Computador (1024 px ou mais) | Painel lateral da altura da janela (`bg-card`, borda à direita, 15rem) com a marca no topo, sobre o fio do cabeçalho de 56 px: a revenda e o papel num bloco com borda, Nova venda como botão primário, grupos com título em 11 px, item de 32 px com ícone (lucide) e, no ativo, fundo `accent`, texto escuro e ícone em `--primary-texto`, sem barra lateral; o menu rola por dentro e a conta fica no pé, abrindo para cima. A Nova venda do título da tela some aqui |

Tabela dentro do cartão vai em `Rolagem` (`componentes/Tela.tsx`). No
computador ela encosta nas bordas do cartão, com o cabeçalho de 36 px em 12 px
sobre o cinza-50 como topo do cartão e linhas de 44 px com fio claro. Abaixo
de 768 px vira cartão (`components/ui/table.tsx`): a primeira coluna é o
título, as outras levam o rótulo da coluna à esquerda do valor e a coluna de
ações (título só para leitor de tela) ocupa a linha toda. Tela nova com tabela
não precisa de nada a mais.

No computador a tabela tem de caber no cartão:
- Linha com mais de uma ação usa `componentes/AcoesDaLinha`: a principal
  (Editar) visível e as outras no botão "⋯"; no celular, fileira de botões.
- Texto longo (nome de cliente, revenda, vendedor) quebra linha com
  `min-w-* whitespace-normal`; a célula padrão não quebra.
- Formulário ao lado da tabela só a partir de `xl` (1280 px):
  `xl:grid-cols-[minmax(0,1fr)_20rem]`.
- O que ainda passar rola dentro do cartão, com a sombra `sombra-de-rolagem`
  na borda. Conferir a 1024 e a 1280 px.
- Lista dentro de cartão é linha com fio (`divide-y`), nunca caixa com borda:
  cartão dentro de cartão não existe.

## Abas e escolha de opção

Aba (`Tabs`) só com painel (`TabsContent`) para cada uma: aba sem painel aponta
`aria-controls` para o vazio e o axe recusa. Para escolher uma opção entre
poucas, como o tipo de desconto ou a forma de pagamento, use
`components/ui/toggle-group.tsx`, com a mesma cara das abas e `aria-label` no grupo.

## Celular primeiro na área do vendedor

- Alvo de toque de 44 px ou mais; botão primário ao alcance do polegar, no pé
  da tela.
- Um passo por tela na Nova venda: os itens (a placa abre uma folha de baixo
  com o QR e o destino; o serviço entra com o pedido mínimo e o passo de
  quantidade), o cliente, a cobrança de tudo numa vez. O total vai no botão do
  pé.
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
   nome, e vira classe no `@theme inline` de `index.css`; nunca valor solto.
3. Componente novo nasce com história no Storybook e passa no axe
   (`npm test` roda cada história no Chromium com o axe em modo erro).
4. Situação nova ganha o tom no mapa de `api/rotulos.ts`.
5. Antes de entregar: `npm run lint` com zero erros, `npm test`,
   `npx storybook build`, os testes de ponta a ponta com axe e o detector do
   impeccable (`DESIGN.md`, Travas).

## Bibliotecas

Conferidas na API do GitHub e no npm em 21/10/2026, pelo critério da skill
`padroes`:

| Biblioteca | Papel | Estrelas | Versão |
| --- | --- | --- | --- |
| [shadcn/ui](https://github.com/shadcn-ui/ui) | Modelo e CLI dos componentes | 126 mil | copiado |
| [Radix Primitives](https://github.com/radix-ui/primitives) | Base acessível dos componentes | 19,4 mil | |
| [Tailwind CSS](https://github.com/tailwindlabs/tailwindcss) | Estilo | 98 mil | 4.x |
| [culori](https://github.com/Evercoder/culori) | A cor da marca escurecida em OKLCH até o contraste | | 4.x |
| [eslint-plugin-better-tailwindcss](https://github.com/schoero/eslint-plugin-better-tailwindcss) | Classe desconhecida e proibida no lint (plugin JS do oxlint) | | |
| [Storybook](https://github.com/storybookjs/storybook) | Catálogo vivo | 91 mil | 10.6.3 |
| [axe-core](https://github.com/dequelabs/axe-core) | Acessibilidade | 7,7 mil | `@axe-core/playwright` 4.14.0 |
| [Playwright](https://github.com/microsoft/playwright) | Teste de ponta a ponta | 98 mil | |
| [Recharts](https://github.com/recharts/recharts) | Gráficos, pelo `components/ui/chart.tsx` do shadcn | 27,6 mil | 3.10.1 |
| [react-easy-crop](https://github.com/ValentinH/react-easy-crop) | Recorte do logo | 2,8 mil | 6.2.4 |
| [Style Dictionary](https://github.com/style-dictionary/style-dictionary) | Tokens em JSON, só se surgir outro consumidor | 4,9 mil | 5.7.0 |

Estado em 24/10/2026: o padrão da categoria aplicado no shell e em todas as
telas do dono, do vendedor e da operadora (DESIGN.md); Início no esboço
aprovado; selos com tons semânticos; tabela encostada no cartão; lint com as
proibições do DESIGN.md; cada história do Storybook no Chromium com o axe em
modo erro; axe no Playwright (`web/e2e`) em todas as telas do dono e da
operadora a 390 e 1440 px e nos fluxos de venda e convite. Toque de 44 px nos
tamanhos de `components/ui/button.tsx` e `input.tsx`.
