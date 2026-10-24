---
version: 1
slug: "web-src"
primary_target: "web/src"
related_targets: []
---

# Surface brief - app inteiro (web/src)

Modo - Operate. Escopo - shell (painel lateral, barra de baixo do celular, cabeçalho), telas do dono, do vendedor e da operadora.

Público e tarefa - vendedor no balcão pelo celular (vender em poucos toques, Pix na tela), dono no computador e no celular (vendas, estoque, financeiro), operadora no computador.

Restrições - white label (logo e cor da revenda por token), WCAG 2.x AA nos testes com axe, 44 px de alvo, texto curto em português.

## Direction contract

THESIS - O padrão da categoria executado sem defeito: o painel de operação que o usuário já sabe usar, no acabamento de Stripe, Linear, Shopify Admin e do app PJ do banco; recusa tanto o template de IA (gradiente, vidro, brilho, roxo, cartão dentro de cartão) quanto o shadcn cru de fábrica.

OWN-WORLD - Escala neutra fria (branco, cinza-zinco em 5 passos, quase preto), uma cor de destaque que é a da revenda e aparece só na ação principal, no item ativo, na seleção e no dado principal do gráfico; estados por cor semântica (verde pago, âmbar aguardando, vermelho falhou) em selo discreto; uma face de interface com números tabulares; borda de 1 px, raio pequeno, grade de 4/8 px, sombra quase nula.

STORY - Quem abre entende na hora onde está o dinheiro, o que está pendente e qual é a próxima ação; o vendedor cobra sem procurar nada; o dono confere o mês sem planilha.

FIRST VIEWPORT - Computador: menu lateral de 240 px (marca da revenda, bloco do papel e da revenda, Nova venda na cor da revenda, grupos do menu, conta no pé), barra de 56 px com sino; conteúdo com título, quatro indicadores em linha, gráfico de 30 dias (7 colunas) ao lado da rosca de até 4 fatias (5 colunas) e a tabela das vendas de hoje com selos de situação. Celular: cabeçalho compacto, barra de baixo com 5 destinos, uma coluna, ação principal fixa acima da barra.

FORM - O padrão da categoria (canon), escolhido pelo dono na rodada de direção; esboço aprovado em .impeccable/mocks/decision/canon.html. seed:33b52b9e045e34fb

SIGNATURE - Movimento curto e funcional: troca de etapa da venda e confirmação do Pix com transição de 150 a 200 ms (View Transition dentro de startTransition), selo que muda de "aguardando" para "paga" sem recarregar, tudo atrás de prefers-reduced-motion.

FINISH - unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance
