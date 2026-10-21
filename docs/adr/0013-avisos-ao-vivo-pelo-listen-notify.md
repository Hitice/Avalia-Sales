# 0013. Avisos ao vivo pelo LISTEN/NOTIFY do Postgres

- Situação - Aceita
- Data - 21/10/2026

## Contexto

O pagamento é confirmado no Worker, mas a tela aberta está ligada ao SignalR
da API. São processos separados, e a stack não tem Redis.

## Opções

- Backplane do SignalR em Redis
- Worker chamando a API por HTTP
- `pg_notify` do Postgres, com a API escutando o canal

## Decisão

`pg_notify` na mesma conexão de quem grava, enviado pelo `SaveChanges`; a API
escuta (`LISTEN`) e repassa ao grupo do SignalR. O aviso leva só o tipo do
fato e o revendedor.

## Consequências

- Dentro de transação, o aviso só sai no commit: venda desfeita não avisa.
- Nenhum serviço novo na infraestrutura.
- Com mais de uma instância da API, cada uma escuta o canal e avisa as próprias
  conexões; o mesmo mecanismo vale sem mudança.
- Aviso perdido numa queda só atrasa a tela até a próxima consulta.
