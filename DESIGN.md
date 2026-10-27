# Design system do Avalia Sales

O padrão da categoria executado sem defeito: o painel de operação que o usuário
já sabe usar, com o acabamento de Stripe Dashboard, Linear, Shopify Admin e dos
apps PJ de banco. Base neutra fria, uma cor de destaque que é a da revenda,
estados por cor semântica, uma face de interface com número tabular, borda de
1 px, raio pequeno, grade de 4 px e sombra quase nula.

Fonte da verdade - `web/src/estilos/tokens.css` (valores) e `web/src/index.css`
(classes, escala de texto, utilitários e movimento). Este documento descreve o
que está no código; se discordarem, vale o código e este documento é corrigido.

## Regras que não se discutem

- Toda cor, raio, sombra e tamanho de texto sai de token. Componente não
  escreve hex, `rgb()`, `oklch()` nem valor solto (`p-[13px]`, `bg-[#123]`).
- A cor da revenda aparece só na ação principal, no item ativo do menu, na
  seleção de texto, no foco e no dado principal do gráfico.
- Estado é cor semântica em selo discreto, nunca a cor da marca.
- Proibido - degradê, vidro e desfoque, brilho, sombra forte, roxo genérico,
  cartão dentro de cartão, ícone em quadradinho colorido, barra colorida de
  mais de 1 px na lateral de cartão ou item, texto em degradê, sobretítulo
  acima do título.
- Texto de tela curto, em português, sem travessão. O título diz o que a tela
  faz, o botão é verbo, o erro diz o que corrigir (seção Texto).
- A ação de todo dia fica à vista: Editar, Pagar e Abrir são botão, não item
  de menu. O "⋯" guarda só o resto.
- Cartão mostra dado. Cartão cujo único conteúdo é um botão ou um link não
  existe: o link vai no cabeçalho (`CardAction`) e o conteúdo é o número, a
  lista ou o gráfico.

## Tokens

### Neutros e superfícies

| Token | Valor | Uso |
| --- | --- | --- |
| `--background`, `--card`, `--popover` | `oklch(1 0 0)` | Fundo da página, do cartão e do menu |
| `--cinza-50` | `oklch(0.985 0 0)` | Cabeçalho de tabela, linha sob o ponteiro, pé do modal |
| `--cinza-100` (`--muted`, `--secondary`, `--accent`) | `oklch(0.967 0.001 286.375)` | Item ativo, aba ativa, esqueleto |
| `--cinza-200` (`--border`) | `oklch(0.92 0.004 286.32)` | Borda de cartão, fio de tabela, série neutra clara |
| `--input` | `oklch(0.64 0.02 285)` | Borda de campo, 3,4:1 sobre branco |
| `--cinza-400` | `oklch(0.705 0.015 286.067)` | Barra de rolagem, ponto do selo neutro, terceira série |
| `--cinza-500` (`--muted-foreground`) | `oklch(0.5 0.016 285.9)` | Texto secundário, 5,6:1 sobre branco |
| `--cinza-700` | `oklch(0.37 0.013 285.805)` | Item do menu fora de foco, texto do selo neutro |
| `--preto` (`--foreground`) | `oklch(0.21 0.006 285.885)` | Texto, 16:1 sobre branco; segunda série do gráfico |

### Marca (white label)

| Token | Padrão da plataforma | Com a marca da revenda |
| --- | --- | --- |
| `--primary` | `oklch(0.488 0.243 264.376)` | A cor primária gravada, inteira |
| `--primary-foreground` | `oklch(1 0 0)` | O texto que a API gravou para a primária (branco ou `#120C2B`) |
| `--primary-hover` | `color-mix(in oklch, var(--primary), black 12%)` | Segue a primária sozinho |
| `--primary-texto` | igual à primária | A primária escurecida até 4,5:1 sobre branco - link, ícone ativo |
| `--chart-1` | `var(--primary)` | A primária escurecida até 3:1 sobre branco - série principal |
| `--chart-2` | `var(--preto)` | `--cinza-500` quando a primária é quase preta (menos de 2:1 contra o preto) |
| `--destaque`, `--ring` | igual à primária | A cor de destaque gravada - foco de teclado |
| `--chart-3`, `--chart-4` | `--cinza-400`, `--cinza-200` | Não mudam |

### Estados

Cada estado tem texto, fundo e borda. Texto sobre o fundo do próprio estado
fica em 4,8:1 ou mais.

| Estado | Texto | Fundo | Borda | Quando |
| --- | --- | --- | --- | --- |
| Sucesso | `oklch(0.527 0.154 150.069)` | `oklch(0.982 0.018 155.826)` | `oklch(0.925 0.084 155.995)` | Paga, entregue, ativa, assinado, no ar |
| Atenção | `oklch(0.555 0.163 48.998)` | `oklch(0.987 0.022 95.277)` | `oklch(0.924 0.12 95.746)` | Aguardando, na carência, a fazer, convite pendente |
| Erro (`--destructive`) | `oklch(0.505 0.213 27.518)` | `oklch(0.971 0.013 17.38)` | `oklch(0.885 0.062 18.334)` | Falhou, cancelada, vencida, suspensa a revenda, atrasado |
| Info | `oklch(0.5 0.134 242.749)` | `oklch(0.977 0.013 236.62)` | `oklch(0.901 0.058 230.902)` | Anda sozinho - pago, em produção, enviado, em andamento, teste em curso |

O tom de cada situação mora em `web/src/api/rotulos.ts` (`TOM_DO_ESTADO`,
`TOM_DO_PEDIDO`, `TOM_DA_ORDEM`, `TOM_DO_PRODUTO`, `TOM_DO_PAGAMENTO`,
`TOM_DO_CONTRATO`) e o da venda em `web/src/painel/situacaoDaVenda.ts`
(`rotuloDaVenda` e `tomDaVenda`, com teste). Tela nova com situação usa esses
mapas, não escolhe cor.

### Raio, sombra e espaço

| Token | Valor | Uso |
| --- | --- | --- |
| `rounded-xs` | 2 px | Marca da legenda, ponta da barra do gráfico |
| `rounded-sm` | 4 px | Teclado, marca quadrada |
| `rounded-md`, `rounded-lg` (`--radius`) | 6 px | Botão, campo, menu, item do menu |
| `rounded-xl` | 8 px | Cartão e modal |
| `rounded-full` | | Selo, avatar |
| `shadow-sm` | `0 1px 2px oklch(0 0 0 / 0.04)` | Aba ativa |
| `shadow-md` | `0 2px 8px oklch(0 0 0 / 0.06)` | Menu suspenso |
| `shadow-lg` | `0 4px 16px oklch(0 0 0 / 0.08)` | Modal e aviso ao vivo |

Espaço na grade de 4 px do Tailwind (`--spacing: 0.25rem`). Grupo apertado
(2 a 8 px), separação larga (16 a 24 px), mais espaço acima do título do que
abaixo. Cartão - 16 px no celular, 16 por 20 px no computador. Página - 16 px
no celular, 24 px no tablet, 24 por 32 px no computador.

## Tipo

Uma face só, Geist (Fontsource, latim e latim estendido, o latim pré-carregado
pelo `vite.config.ts`). Monoespaçada só em código e chave de recuperação. Todo
número é tabular (`font-variant-numeric: tabular-nums` no corpo). Pesos 400 e
500; 600 só em total de dinheiro destacado. Títulos com `letter-spacing:
-0.02em` e `text-wrap: balance`.

| Classe | Tamanho e linha | Uso |
| --- | --- | --- |
| `text-2xs` | 11 / 16 px | Eixo de gráfico, grupo do menu, papel da revenda, barra de baixo |
| `text-xs` | 12 / 16 px | Cabeçalho de tabela, selo, detalhe da linha |
| `text-sm` | 13 / 20 px | Corpo, célula, título de cartão (500) |
| `text-md` | 14 / 20 px | Campo no computador |
| `text-base` | 16 / 24 px | Campo no celular (não deixa o iPhone ampliar) |
| `text-lg` | 18 / 28 px | Destaque raro |
| `text-xl` | 20 / 28 px | Título da tela (500) |
| `text-2xl` | 24 / 32 px | Valor do indicador (500) |
| `text-3xl` | 30 / 36 px | Reservado |

A paleta, as fontes, as sombras, os raios e a escala de texto padrão do
Tailwind estão desligados em `index.css` (`--color-*: initial` e os outros):
`bg-purple-500`, `shadow-xl` ou `text-4xl` não compilam e o lint recusa.

## Componentes e estados

- Shell (`app/Moldura.tsx`) - computador (1024 px ou mais): painel lateral de
  240 px com a marca da revenda no topo (sobre o fio do cabeçalho, uma coluna
  só), o bloco do papel e da revenda em borda, Nova venda primária, grupos com
  título em 11 px, item de 32 px com ícone de 16 px (ativo - fundo
  `--accent`, texto escuro, ícone em `--primary-texto`), conta no pé abrindo
  para cima, e o botão Recolher menu abaixo dela: recolhido, o painel fica
  com 60 px, só ícones com `title` e rótulo para o leitor de tela, a marca
  vira o ícone e a escolha fica no navegador (`avalia.menu-recolhido`).
  Cabeçalho de 56 px com fio e o sino. Tablet - menu de cima
  quebrando linha, item de 36 px. Celular - cabeçalho compacto, barra de baixo
  com 4 destinos e "Mais" (folha que sobe de baixo), alvo de 52 px, ícone ativo
  na cor da marca e traço de 2 px em cima.
- Título da tela (`Cabecalho` em `componentes/Tela.tsx`) - h1 de 20 px, um
  detalhe curto ao lado (a data no Início) e as ações à direita. A Nova venda
  do título some no computador, onde o painel lateral já a tem.
- Indicador (`componentes/Indicador.tsx`) - rótulo de 13 px em cinza e valor
  de 24 px; dois por linha no celular, até quatro no computador. Valor de
  dinheiro não é cortado. Destaque (comissão a receber) é o valor em verde,
  sem anel nem fundo.
- Cartão (`components/ui/card.tsx`) - borda de 1 px, raio de 8 px, sem
  sombra. Cartão dentro de cartão não existe: lista dentro de cartão é linha
  com fio (`divide-y`), e seção à parte (as retiradas de sócio na DRE) é fio
  em cima (`border-t`), nunca caixa em `bg-muted`. A ação do cabeçalho vai em
  `CardAction`, sempre um botão `size="sm"` (link, contorno ou primário): o
  componente centra na linha do título e desconta a altura, para o cabeçalho
  continuar com 20 px e os títulos dos cartões vizinhos ficarem na mesma
  linha. Cabeçalho com `flex` próprio para pôr o botão não entra.
- Tabela (`components/ui/table.tsx` dentro de `Rolagem`) - no computador,
  encosta nas bordas do cartão, cabeçalho de 36 px em 12 px sobre o cinza-50
  (vira o topo do cartão), linha de 44 px com fio cinza-100 e cinza-50 sob o
  ponteiro; com título no cartão, o título ganha o fio de baixo. O tipo da
  coluna vai só no cabeçalho (`TableHead tipo`): `numero` à direita, `data`,
  `numero` em algarismos de mesma largura, `situacao` com os selos da coluna
  na largura do maior e o texto de apoio embaixo, `acoes` na última coluna;
  número, data, situação e ações ficam na largura do conteúdo e o resto vai
  para o texto. A coluna de ações fica presa à direita (`position: sticky`,
  fundo do cartão): quando a tabela é mais larga que o cartão e rola, a ação
  continua na tela. Ação da linha é sempre `AcoesDaLinha`: a principal
  (Editar, Pagar) em botão `sm` à vista e o resto no "⋯"; botão e formulário
  dentro da célula não entram (a ação abre um modal). Texto opaco e longo
  (id de recebedor) fica em `truncate` com `title`, para não alargar a
  tabela. Lista longa rola dentro da tabela com o cabeçalho fixo e, a partir
  de sete linhas, ganha a busca. No celular, cada linha vira um cartão com o
  rótulo da coluna à esquerda, a primeira ação em botão e o resto no "⋯"; a
  lista corre com a página, sem rolagem própria. A área que rola só vira parada de tabulação quando
  rola e não tem link nem botão (`useRegiaoRolavel`).
- Lista paginada pela API (`componentes/ListaPaginada.tsx`, `lib/paginacao.ts`) -
  toda lista que cresce com o uso: o campo de busca no alto (o termo vai à API
  depois de 300 ms parado, e a tabela leva `semBusca`), a tabela e o pé
  `Paginacao` ("51 a 100 de 230", Anterior e Próxima a 44 px no celular, e a
  página aberta fica esmaecida enquanto a seguinte chega). Estados: carregando
  (`Carregando`), vazio ("Nenhuma venda ainda." ou `Nada na lista com "x".`) e
  erro (`Erro`). Número que resume a lista inteira vem de rota própria, e não
  da soma da página.
- Selo (`components/ui/badge.tsx`) - 22 px, raio cheio, 12 px em 500, ponto
  de 6 px na cor do texto (no `neutro`, cinza-400). Variantes `sucesso`, `atencao`, `erro`, `info` e
  `neutro` para situação; `default` (cor da marca) só para contagem, como itens
  na venda e mensagens não lidas; `outline` e `secondary` para rótulo sem
  situação (tipo, categoria, mínimo).
- Botão (`components/ui/button.tsx`) - 44 px no celular e 36 px no computador
  (`lg` 48 e 40 px). Principal na cor da marca, escurece 12% sob o ponteiro;
  contorno, discreto e link (em `--primary-texto`); destrutivo nos tokens de
  erro. Desligado a 50%. Foco em anel sólido de 3 px no `--ring`.
- Campo (`componentes/Campo.tsx` sobre `components/ui/field.tsx`) - rótulo
  em 13 px 500 em cima, borda `--input`; no foco, borda e halo de 3 px no
  `--ring`; conferido, verde com o ícone; com erro, vermelho e a mensagem
  embaixo, ao sair do campo ou ao enviar. Placeholder em caixa normal mesmo em
  campo maiúsculo.
- Vazio (`Vazio`, ou `EstadoDaConsulta` com `acao`) - uma linha que diz o que
  falta e, quando houver, uma ação que resolve (Nova venda, Pedir placas,
  Cadastrar vendedor).
- Carregando - `Carregando` (linhas de 44 px pulsando) só onde a espera é
  real, no lugar da lista; `CarregandoTela` quando nada aparece sem o dado;
  barra de 4 px no topo da janela depois de 300 ms de consulta.
- Modal (`components/ui/dialog.tsx`) - borda, `shadow-lg`, raio de 8 px,
  sobreposição a 20% do preto sem desfoque, pé no cinza-50; abre com o foco
  nele mesmo; botão de fechar "Fechar". Folha de baixo no celular (Mais, placa
  da venda) sobe da borda, sem borda lateral.
- Aviso ao vivo - caixa escura com texto claro, `aria-live="polite"`; no
  celular logo abaixo do cabeçalho, no computador no canto de baixo à direita;
  some em 5 s.
- Gráfico - série principal em `--chart-1`, barra fina com ponta de 2 px, dia
  zerado num traço cinza de 2 px, grade só horizontal, eixo em 11 px, tooltip
  e a mesma informação numa tabela `sr-only`. Rosca de até quatro partes
  (`Rosca` em `financeiro/Graficos.tsx`): com `primeira`, essa parte fica
  sempre em `--chart-1` com o percentual no centro, e sem ela no mês a cor da
  marca não vai para outra parte.
- Início do dono (`painel/TelaInicial.tsx`) - o esboço aprovado: quatro
  indicadores, os últimos 30 dias de vendas (7 de 12 colunas) ao lado do
  Financeiro do mês (5 colunas; recebido, comissões, custo das placas, parte
  da Avalia no mês com serviço, despesas e resultado, as mesmas linhas da DRE em `financeiro/LinhaDaDre`,
  com Abrir no cabeçalho) e a tabela de hoje (hora, cliente, item, valor,
  situação). No celular, uma coluna e a lista de hoje em extrato.
- Financeiro do dono (`financeiro/TelaDoFinanceiro.tsx`) - quatro
  indicadores; depois, em 12 colunas, o Demonstrativo (5), as Saídas em rosca
  (4) e o Caixa com as Despesas (3); o Histórico de seis meses ocupa a linha
  de baixo inteira. O saldo do caixa tem o mesmo tipo do indicador (24 px,
  500).

## Texto

Rótulo, selo, menu, coluna, botão e título de cartão:

- Uma palavra quando basta ("Aguardando", "Paga", "Bloqueados", "Caixa",
  "Saídas"); duas ou três quando uma só tira o sentido ("Custo das placas").
  Sem frase, sem artigo, sem a pergunta no lugar do nome ("Para onde foi o
  dinheiro" é "Saídas").
- Botão é verbo no infinitivo ("Pagar", "Editar", "Abrir"); o objeto só
  quando a tela tem mais de um ("Fechar mês", "Nova retirada").
- Selo de situação é uma palavra do mapa de `rotulos.ts`.
- Sem subtítulo explicativo, ajuda de campo ou parágrafo que só descreva a
  tela. Texto de apoio existe quando muda a decisão de quem lê ("Sem
  pagamento em 7 dias, a placa sai do ar.").

Mensagem de erro, da API (`RegraDeNegocioException`) ou da tela:

- Fato primeiro, sem artigo e sem "está": "Mês fechado.", "Código não
  confere.", "Pedido com link aberto."
- Depois, se houver saída, uma frase no imperativo: "Use outra data.",
  "Ligue em Segurança.", "Fale com o suporte."
- Até duas frases, cerca de 80 caracteres. Sem o porquê da regra, sem "sua
  conta", "você", "por favor", "tente de novo", "não foi possível".
- Nome do objeto quando identifica (lote 12, placa K7M2PX, outubro), nunca
  id interno. Valor e limite no formato da tela (R$ 1.234,56).
- A mesma palavra para a mesma coisa em todas: revenda, vendedor, cliente,
  placa, pedido, lote, venda, cobrança, pagamento por fora, segundo fator,
  operadora, suporte.

## White label e contraste

A revenda escolhe na tela Marca a cor primária, a de destaque, o nome e o logo.
`variaveisDaMarca(primaria, destaque, textoSobrePrimaria)` em
`web/src/marca/marca.ts` transforma isso nas variáveis acima, e `aplicarMarca`
grava em `:root` depois do login, sem recompilar nada. A prévia da tela Marca
usa a mesma função.

Como o contraste é derivado (luminância relativa da WCAG 2.2, a mesma conta em
`web/src/marca/contraste.ts` e no `Contraste.cs` da API):

1. Antes de salvar, a tela e a API recusam primária que não dá 4,5:1 com o
   texto por cima (branco ou `#120C2B`, o de maior contraste) e destaque com
   menos de 3:1 sobre branco.
2. O texto sobre a primária é o que a API gravou (`textoSobrePrimaria`).
3. `--primary-texto` é a primária escurecida em OKLCH, um centésimo de
   luminosidade por vez com o matiz e o croma de origem, até 4,5:1 sobre
   branco (`legivelSobreBranco`). Primária que já passa fica como está.
4. `--chart-1` é a mesma conta até 3:1, o mínimo para marca de dado.
5. Com primária quase preta, a segunda série do gráfico desce ao cinza-500
   para não sumir na primeira.
6. O item ativo do menu fica em fundo neutro: a marca entra só no ícone, então
   nenhuma cor de revenda deixa o texto ilegível.

Logo da revenda passa pelo recorte (horizontal 3:1 ou quadrado) e vai no topo
do painel lateral e no cabeçalho do celular; sem logo, o nome; sem nome, o
logo da plataforma (`web/public/marca`). Login e área da operadora usam a
marca da plataforma.

## Movimento

- Curto e funcional: 150 a 200 ms (`--duracao-curta`, `--duracao-media`), saída
  exponencial (`--curva-saida`, classe `ease-saida`). Nada passa de 200 ms.
- CSS primeiro - `transition-colors duration-150` em botão, item e linha;
  entrada pelo `@starting-style` (variante `starting:` do Tailwind) no aviso ao
  vivo; folha e modal pelo `tw-animate-css` em 150 ms.
- Dois momentos com View Transition, pelo `<ViewTransition>` do React 19.3
  dentro de `startTransition`: a troca de etapa da Nova venda (a etapa sai 8 px
  para a esquerda em 150 ms e a nova entra em 180 ms) e a troca do QR do Pix
  pela confirmação (200 ms). O QR da primeira resposta aparece na hora: só a
  mudança de situação passa pela transição.
- `prefers-reduced-motion` - animação e transição caem a 0,01 ms e as View
  Transitions ficam sem animação; esqueleto e ícone girando param
  (`motion-reduce:animate-none`).

## Superfícies do navegador

Seleção de texto na primária a 20%, cursor do campo na primária, foco de
teclado em contorno sólido de 2 px no `--ring` (o "auto" do Safari ignora a
cor), barra de rolagem no cinza-400, sublinhado com 3 px de distância, número
tabular em toda tabela.

## Travas e como rodar

Tudo dentro de `web/`:

| Comando | O que trava |
| --- | --- |
| `npm run lint` | oxlint com `eslint-plugin-better-tailwindcss` (classe desconhecida pelo `index.css`, classe repetida, e `no-restricted-classes` - degradê, `blur` e `backdrop-blur`, sombra fora de `shadow-sm/md/lg`, roxo, violeta, fúcsia e índigo, valor solto `-[...]` e propriedade solta `[prop:valor]`) e a regra própria `design-system/sem-cor-solta` (`web/lint/design-system.js` - cor em `style={{}}` e hex, `rgb`, `hsl` ou `oklch` em texto de `.tsx`, fora de `src/marca`, histórias e testes). Zero erros para passar |
| `npm test` | Vitest em dois projetos - `unidade` (jsdom) e `historias` (cada história do Storybook no Chromium com o axe em modo erro, contraste incluído) |
| `npm run build-storybook` | O catálogo compila (o CI também compila) |
| `npm run e2e` | Ponta a ponta com `@axe-core/playwright` (WCAG 2.2 AA, violação séria ou crítica falha) em todas as telas do dono, do vendedor e da operadora a 390 e 1440 px, sem rolagem lateral, e os fluxos de venda, cartão, convite, jornada, segurança e encerramento a 390 px. Precisa da API local com a demonstração (`CLAUDE.md`, Rodar e testar) |

`npm test` e `npm run e2e` usam o Chromium do Playwright: `npm run navegador`
instala uma vez.

Opcional, para quem tem a skill impeccable instalada na própria máquina (ela
não fica no repositório): do topo,
`~/.claude/skills/impeccable/scripts/impeccable detect --json web/src` lê os
padrões de template. Ele aponta a Geist como fonte comum demais; a escolha é
deliberada (o padrão da categoria) e fica registrada aqui.

## Como acrescentar

1. Procurar no Storybook e no shadcn/ui: o componente quase sempre existe.
2. Cor, raio, sombra ou tamanho novo entra como token em `tokens.css` (com o
   papel no nome) e vira classe no `@theme inline` de `index.css`.
3. Situação nova ganha o tom no mapa de `rotulos.ts`.
4. Componente novo nasce com história no Storybook e passa no axe.
