# 0034. Venda digital parcial com acesso por grupo

- Situação - Aceita
- Data - 19/11/2026
- Substitui - 0033

## Contexto

A produção imprime lotes maiores que os pacotes vendidos no Mercado Livre,
TikTok Shop e Shopee. A baixa precisa aceitar só os códigos do pedido e manter
o saldo do lote disponível para pedidos futuros. O comprador deve receber um
acesso que alcance apenas as placas enviadas a ele.

## Decisão

- Um lote é estoque de códigos impressos disponíveis; não ganha código de
  ativação na geração.
- Na venda, a operadora informa os códigos separados, revenda, valor total
  recebido, canal e pedido. O servidor normaliza e valida os códigos, trava o
  lote, recusa placa fora do lote, duplicada, já separada ou pertencente a outra
  revenda, e grava dentro da transação.
- Cada pedido vira um `GrupoDeAtivacao` com código de 6 caracteres único,
  associado às placas em `placas_emitidas`. A tabela global guarda apenas
  identificadores de roteamento; os dados financeiros do grupo ficam em tabela
  com RLS. `/ativacao/{codigo}` resolve a revenda pela tabela de códigos,
  assume seu escopo e retorna/ativa apenas as placas do grupo.
- O valor total do grupo entra como uma receita no razão da revenda; a ativação
  divide esse valor entre as placas sem perder centavos. A baixa gera também o
  código e o link direto `/ativar?codigo=...`.
- Cancelamento/devolução revoga o acesso e estorna a receita. Placas não
  ativadas voltam ao estoque da mesma revenda. Placas ativadas têm a venda
  cancelada e o QR encerrado para sempre; a reposição usa outro código.
- Os códigos de ativação e as vendas digitais dos lotes antigos continuam
  sendo lidos pelo fluxo anterior.

## Consequências

- A baixa exige informar os códigos físicos do pedido. A ação é protegida pelo
  segundo fator da operadora e as placas restantes não são alteradas.
- O código do grupo é um segredo operacional: quem o possui pode ativar e
  trocar o destino das placas selecionadas. O limite público por IP continua
  valendo.
- O cancelamento após ativação precisa seguir o fluxo de encerramento e
  reposição, para impedir que um QR já conhecido seja vendido a outro cliente.
- (20/11/2026) A receita do grupo é de placa na DRE, em 12 partes a partir da
  baixa, com o custo das placas guardado no grupo no dia da venda, para a
  revenda que vende no digital placas que comprou no pedido. O código
  devolvido vai a `codigos_reservados`. A placa separada fica travada no
  estoque até a ativação ou a devolução, e o lote com placa no digital não
  vai num pedido de placas.
- Venda digital para revenda que não é do grupo da operadora: o canal paga a
  Avalia, mas a receita entra no razão da revenda, e não há acerto da Avalia
  para a revenda (o "a acertar" da ADR 0028 só vai da revenda à Avalia).
  Decisão pendente: restringir a venda digital às revendas do grupo, ou criar
  esse acerto.
