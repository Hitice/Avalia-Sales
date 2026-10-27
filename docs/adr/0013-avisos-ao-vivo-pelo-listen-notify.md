# 0013. Avisos ao vivo pelo LISTEN/NOTIFY do Postgres

Quem confirma o pagamento é o Worker, mas a tela aberta está ligada à API. O
aviso passa pelo próprio Postgres: ao salvar, a mesma conexão envia um NOTIFY,
a API escuta o canal (LISTEN) e avisa as telas conectadas pelo SignalR. Dentro
de transação, o aviso só sai se ela confirmar.

- Situação - Aceita
- Data - 21/10/2026

## Contexto

O pagamento é confirmado no Worker, mas a tela aberta está ligada ao SignalR
da API. São processos separados, e a stack não tem Redis.

## Opções

- Backplane do SignalR em Redis - um serviço a mais na infraestrutura só para
  isso
- Worker chamando a API por HTTP - o Worker passa a depender da API no ar
- `pg_notify` do Postgres, com a API escutando o canal

## Decisão

`pg_notify` no canal `avisos_ao_vivo`, na mesma conexão de quem grava, enviado
pelo `SaveChangesAsync` depois de gravar. A API escuta o canal
(`OuvinteDeAvisos`) e repassa ao grupo do SignalR do revendedor ou da
operadora. O aviso leva só o tipo do fato, o revendedor e se vai à operadora;
a tela refaz a própria consulta pela API, que aplica o isolamento.

## Consequências

- Dentro de transação, o aviso só sai no commit: venda desfeita não avisa.
- Nenhum serviço novo na infraestrutura; o Postgres já está nela.
- Com mais de uma instância da API, cada uma escuta o canal e avisa as próprias
  conexões, sem mudança.
- Aviso perdido numa queda só atrasa a tela até a próxima consulta. A escuta
  reconecta sozinha, com espera crescente até 60 segundos.
