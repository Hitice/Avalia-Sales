# 0033. Venda digital e ativação pelo comprador com o código do lote

- Situação - Aceita
- Data - 17/11/2026

## Contexto

Lotes de placas vendidos no Mercado Livre, no TikTok Shop e na Shopee chegam ao
comprador sem passar por revenda nem pelo balcão. O comprador precisa ligar a
placa ao estabelecimento no Google, ou o lote de cardápio ao link do cardápio,
sozinho e sem conta na plataforma. A regra "placa sem venda não vai ao ar"
continua valendo, e o dinheiro já foi recebido no canal.

## Opções

- Código de 6 caracteres por lote, rota pública que só responde com ele, e venda
  nascida na ativação, já paga
- Conta do comprador com e-mail e senha
- Placa no ar sem venda, marcada como do canal

## Decisão

A primeira.

- Código - `lotes.codigo_de_ativacao`, sorteado na geração do lote com o
  alfabeto da placa (Crockford, 32 símbolos: cerca de um bilhão de códigos),
  único entre lotes, placas e reservados. O código é a única chave: quem o tem
  ativa e troca o destino das placas do lote.
- Baixa manual - a operadora lança a venda digital em Lotes, com a revenda (a
  do grupo dela vem escolhida), o valor recebido, o canal e o pedido. A gravação
  abre o banco da revenda escolhida (`IGravacaoNaRevenda`, no modelo do ADR
  0032), põe as placas no estoque com custo zero e lança a receita no razão,
  numa linha com origem no lote. Exige o segundo fator (ADR 0027).
- Rota pública - `/ativacao/{codigo}` acha o lote na tabela da plataforma, sem
  RLS, e só então abre um escopo que assume a revenda dona, como a leitura do QR.
  Código errado, lote arquivado e lote sem venda digital respondem igual. A
  busca no Google (cota da Avalia) só responde com código válido. Limite por IP
  de 30 chamadas por minuto contra quem tentar adivinhar.
- Venda - na ativação nasce uma venda por placa (`vendas.do_canal_digital`), no
  nome do dono, sem comissão e com a parte da placa no valor do lote. Ela não
  lança dinheiro, não se cobra, não se cancela nem vira cortesia. A placa vale
  12 meses da ativação e renova pelo fluxo de sempre.

## Consequências

- O código vale enquanto o lote não for arquivado; não há troca do código nem
  expiração. Um código vazado deixa trocar o destino das placas daquele lote:
  arquivar o lote corta o acesso.
- A ativação não conta no limite diário de destinos da revenda; a lista de
  destinos bloqueados e o Google Web Risk valem.
- O cancelamento da venda digital ainda não existe: devolução no canal se
  resolve hoje pela operadora, fora da tela.
