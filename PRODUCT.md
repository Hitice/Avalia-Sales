# Product

<!-- impeccable:product-schema 1 -->

Contexto de produto para a revisão de design (skill impeccable). Regra de
negócio e jornada completas em `docs/PDD.md`; arquitetura em `docs/SDD.md`;
tokens e componentes em `DESIGN.md`. Este arquivo não repete regra: resume quem
usa, em que situação e o que a marca exige.

## Platform

web

## Users

- **Vendedor** - equipe da revenda. Vende no balcão do cliente pelo celular, em pé e com pressa: escolhe o item, lê o QR da placa, grava o NFC, cadastra o cliente e cobra por Pix ou cartão na hora.
- **Revendedor** - empresa que revende. No computador e no celular: compra placas, convida e comissiona a equipe, define preço, acompanha vendas, estoque, mapa, comissões e o financeiro.
- **Operadora** - a empresa que opera a plataforma (hoje a Avalia). Produz e despacha lotes, cuida do catálogo, executa os serviços vendidos, atende chamados e administra as revendas (recebedor, suspensão, encerramento). Não abre os dados de venda das revendas.
- **Cliente final** - o comércio que compra a placa. Não usa o painel: paga o Pix na tela do vendedor e usa a placa no negócio.

## Product Purpose

Plataforma para uma rede de revendas vender placas com QR Code e NFC (que levam a uma avaliação no Google, a um link ou a uma página de links) e serviços que a operadora executa. Cada revenda vende com a própria equipe, recebe pelo Pagar.me com split e acompanha tudo num painel. Sucesso: a venda no balcão sai em poucos toques, o dinheiro e a comissão fecham sem planilha, e a placa vendida responde para sempre.

## Positioning

O físico inicia e o digital continua: a placa sai de um estoque com código único, a venda grava o destino e o NFC no balcão, e o mesmo sistema cobra, divide o dinheiro, comissiona, renova e mede as leituras. A operadora produz as placas e executa os serviços; a revenda vende.

## Operating Context

- Venda no balcão, no celular, muitas vezes ao sol e com o cliente esperando o Pix na tela.
- Gestão no computador e no celular pelo revendedor; operação diária pela operadora no computador.
- Integrações - Pagar.me (Pix, cartão, split, estorno), Google Places e Maps, D4Sign (contrato), Resend (e-mail), BrasilAPI (CNPJ e CEP), Gotenberg (PDF).
- Multi-cliente - a plataforma atende várias redes, e cada revenda aplica a própria marca (logo e cores) por tokens.

## Capabilities and Constraints

- Em produção em sales.avaliaone.com.br. O que a plataforma faz está no `docs/PDD.md` (seções de jornada e regras); o que está em desenvolvimento não aparece na tela.
- Front - React 19, Vite, Tailwind 4, shadcn/ui, TanStack Query; Storybook e Playwright (com axe) na verificação.
- Telas da operação priorizam velocidade e leitura; a plataforma pesa igual o celular do vendedor e o painel do revendedor.

## Brand Commitments

- A plataforma se chama Avalia Sales. A marca de cada revenda entra por tokens (white label) e não pode quebrar contraste nem layout.
- Tom - preciso, direto, prático; português técnico, frases curtas, sem travessão, sem superlativo nem promessa sem evidência.
- Direção visual - o padrão da categoria, executado sem defeito e sem ironia: convenção de painel de operação, nada de mundo temático. Régua de acabamento - Stripe Dashboard (dinheiro, tabelas, estados de pagamento), Linear (velocidade, tipografia, movimento curto), Shopify Admin (operação de loja no celular e no computador) e os apps PJ de banco digital (Pix, saldo e extrato no celular).
- Proibido - visual de template de IA (gradiente, vidro, brilho, roxo genérico, cartão dentro de cartão, ícone em quadradinho colorido) e qualquer coisa que quebre a marca da revenda (cor que some, contraste baixo, layout que briga com o logo). A lista completa e o lint que a cobra estão em `DESIGN.md`.

## Evidence on Hand

- Produção real com revendas, vendedores e placas vendidas.
- Fotos dos produtos do catálogo em `web/public/produtos`.
- Nenhum depoimento, caso ou número de mercado documentado para uso em tela.

## Product Principles

1. A venda no balcão vem antes de tudo: nenhum refinamento pode custar toque, espera ou leitura.
2. Dinheiro é exato e rastreável: valor, estorno e comissão aparecem como o razão registra, nunca arredondados para parecer melhor.
3. Todo registro criado pode ser editado ou desfeito na tela.
4. A marca é do cliente: a base é neutra o bastante para vestir qualquer revenda.
5. O que funciona hoje aparece; o que está em desenvolvimento não.

## Accessibility & Inclusion

Manter o que existe: testes de acessibilidade com axe nas telas do revendedor e da operadora a 390 e 1440 px, alvos de toque de 44 px e foco visível. Sem meta formal além disso.
