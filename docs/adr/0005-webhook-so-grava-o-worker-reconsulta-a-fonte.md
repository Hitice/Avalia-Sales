# 0005. Webhook só grava; o Worker reconsulta a fonte

- Situação - Aceita
- Data - 21/10/2026

## Contexto

O Pagar.me não assina o webhook, e o HMAC da D4Sign cobre só o UUID do documento.

## Opções

- Aplicar o corpo do webhook
- Gravar o evento cru e reconsultar a API de origem

## Decisão

A rota grava em `eventos_externos` com `on conflict do nothing` e responde 200. O Worker acha o revendedor pelo vínculo, reconsulta a fonte fora de transação e aplica com o evento travado por `for update skip locked`. Uma conferência periódica de cobrança pendente cobre o webhook perdido.

## Consequências

- Reenvio não duplica efeito.
- Corpo forjado não muda nada: quem decide é a API de origem.
