# 0032. Placa personalizada com códigos gerados na ordem de serviço

- Situação - Aceita
- Data - 31/10/2026

## Contexto

A placa personalizada é feita sob encomenda, com a arte do cliente e mais de um
QR (avaliação e Instagram, cardápio e Linktree), e às vezes aponta para links
que o cliente já tem. Ela não sai do estoque da revenda: só existe depois da
venda paga. O modelo da venda tinha uma placa por venda, e renovação, aviso de
vencimento, cancelamento e troca partiam dessa placa.

## Opções

- Serviço com QR, códigos de placa gerados pela operadora na ordem de serviço
- Duas linhas de placa do estoque na mesma venda
- QR fixo apontando direto para o link do cliente, sem código da plataforma
- Tipo de produto novo

## Decisão

A primeira, sem tipo novo de produto.

- Produto - serviço do catálogo da Avalia com `codigos_na_placa` de 1 a 4. Sai
  de uma em uma, sem teste gratuito, com a parte da Avalia como os outros
  serviços. O tipo continua `Servico`, e a versão anterior lê o banco novo
  como um serviço comum.
- Venda - a linha leva um destino por QR (`destinos_da_venda`, com RLS), sem
  código. Cada destino passa pela lista da operadora e pelo Google Web Risk e
  conta no limite de destinos do dia.
- Ordem - com a venda paga, a ordem nasce como a dos outros serviços e grava
  quantos QR e os destinos de cada um, como a venda grava o preço: mudar o
  catálogo depois não muda a ordem aberta, e a operadora monta a arte pelos
  destinos sem ler a tabela da revenda. Na ordem, a operadora gera os códigos: um lote só dessa placa, já da revenda que
  vendeu, sem pedido, despacho nem ativação (`Lote.EntregarParaOrdem`), com um
  código por destino, na ordem informada. As placas nascem vendidas, com custo
  zero e no ar, para a arte ser conferida antes da entrega. O primeiro código
  vira o da venda (`vendas.placa_id`); os outros apontam para ele
  (`placas.placa_principal_id`). O QR sai pela impressão do lote.
- Entrega - marcar a ordem como entregue exige os códigos gerados e começa os
  12 meses de todos os códigos. A renovação só abre depois da entrega, e a
  ordem reaberta e entregue de novo nunca encurta o vencimento.
- Depois - cada código tem o destino dele e liga e desliga sozinho. A
  renovação é uma por placa, pelo valor da venda, e pode ser pedida por
  qualquer código: o adicional renova pelo principal, e o pagamento ou o
  desfazer estende ou volta todos. O aviso de vencimento sai só para o
  principal. O cancelamento tira todos do ar e não devolve ao estoque, porque
  a arte é do cliente, e a baixa deles não se desfaz. A troca de código é
  recusada: refazer a placa é pedir outra à Avalia. Cancelar a ordem não mexe
  nos códigos: quem tira a placa do ar é o cancelamento da venda.
- Escrita da operadora na revenda - os códigos nascem no estoque da revenda,
  que o RLS fecha para a operadora. `IEscopoDaOrdem` abre o banco com a revenda
  da ordem na sessão do Postgres, lida da própria ordem e não de quem chama,
  numa transação só com a ordem, presa com `for update`, e recusa quem não
  entra como operadora. Gerar os códigos pega também a trava da cobrança da
  venda, a mesma do cancelamento. É o único caminho de gravação da operadora em
  tabela da revenda fechada para ela.

Duas linhas de placa do estoque foram descartadas: dariam duas renovações, cada
linha esbarra na tabela da placa comum, e a placa sob encomenda não está no
estoque. QR fixo perde o que a plataforma vende: trocar o destino sem refazer a
placa, contar leitura e renovar. Um tipo novo de produto quebraria a leitura do
catálogo na versão anterior.

## Consequências

- Vendas, carteira, mapa e ficha do cliente mostram o código principal: a placa
  física é uma. O estoque e o distrato listam cada código.
- A transferência no encerramento leva todos os códigos; o adicional continua
  renovando pelo principal na revenda receptora.
- Um código da placa personalizada desligado não desliga os outros.
