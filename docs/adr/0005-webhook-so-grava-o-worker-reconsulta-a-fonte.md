# 0005. Webhook só grava; o Worker reconsulta a fonte

O webhook só registra o aviso. O Worker consulta a API de origem antes de mudar
qualquer dado, então aviso repetido não repete efeito e aviso falso não muda
nada. Uma conferência periódica recupera o aviso perdido.

- Situação - Aceita
- Data - 21/10/2026

## Contexto

A documentação do webhook do Pagar.me v5 não traz assinatura do corpo. O HMAC
da D4Sign cobre só o UUID do documento, não o corpo.

## Opções

- Aplicar o corpo do webhook
- Gravar o evento cru e reconsultar a API de origem

## Decisão

- Entrada - `/webhooks/pagarme/{segredo}` e `/webhooks/d4sign`. O Pagar.me
  chega com um segredo na URL (`Pagarme:SegredoDoWebhook`, comparado em tempo
  constante; segredo errado responde 404). A D4Sign chega com o HMAC do UUID no
  cabeçalho `Content-Hmac`.
- Gravação - a rota grava o evento cru em `eventos_externos` com
  `on conflict (origem, id_externo) do nothing` e responde 200.
- Processamento - o Worker (`ProcessadorDeEventos`) acha o revendedor pelo
  vínculo, reconsulta a fonte fora de transação e aplica o efeito com o evento
  travado por `for update skip locked`, na mesma transação que o marca como
  processado. Evento que falha volta à fila, até 10 tentativas.
- Conferência - a cobrança pendente é reconsultada a cada
  `Pagarme:ConferenciaEmSegundos` (300 em produção); a paga, uma vez por dia
  durante 120 dias, para pegar chargeback e estorno feito no painel do
  Pagar.me; o contrato na D4Sign, a cada 30 minutos.

## Consequências

- Reenvio não duplica efeito.
- Corpo forjado não muda nada: quem decide é a API de origem. O segredo na URL
  só barra quem não conhece o endereço.
- Evento de pedido ou documento sem vínculo na plataforma é encerrado sem
  efeito.
