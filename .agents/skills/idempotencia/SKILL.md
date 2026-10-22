---
name: idempotencia
description: Como o Avalia Sales recebe webhook, chama API externa e roda rotina no Worker sem cobrar, comissionar ou enviar duas vezes. Usar antes de mexer em webhook do Pagar.me, da D4Sign ou do Resend, em retentativa, em fila do Worker ou em qualquer efeito que não pode se repetir.
---

# Idempotência no Avalia Sales

Toda entrega de fora chega pelo menos uma vez, e às vezes mais. Webhook chega
repetido, fora de ordem ou atrasado. Requisição de saída cai no meio e não se
sabe se valeu. A regra é tornar o efeito repetível sem dano, e não confiar que
a repetição não acontece.

Não usamos biblioteca de idempotência pronta: a mais conhecida para ASP.NET
Core, [IdempotentAPI](https://github.com/ikyriak/IdempotentAPI), tinha 329
estrelas e nenhum commit desde 11/2024 quando conferimos (21/10/2026). O padrão
abaixo usa o próprio Postgres, que já garante unicidade e transação.

## Entrada: webhook

1. **Grava antes de processar.** O evento cru vai para `eventos_externos`, com
   índice único em `(origem, id_do_evento)`:
   ```sql
   insert into eventos_externos (id, origem, id_do_evento, corpo, recebido_em)
   values (@id, @origem, @evento, @corpo::jsonb, now())
   on conflict (origem, id_do_evento) do nothing;
   ```
   Zero linhas inseridas = repetido. Responde 200 e para.
2. **Responde rápido.** 200 logo depois de gravar; o Worker processa a fila. O
   remetente não fica esperando e não reenvia por tempo esgotado.
3. **Reconsulta a fonte.** O Worker busca o pedido na API de origem antes de
   mudar a situação. O corpo do webhook não é fonte de verdade: pode estar
   velho, fora de ordem ou ser forjado. A página de webhooks do Pagar.me não
   documenta assinatura (ver a skill `pagarme-checkout-order`).
4. **Efeito e marca na mesma transação.** Pagamento confirmado gera comissão,
   lançamento no razão e `processado_em` do evento numa transação só. Se cair no
   meio, nada fica pela metade e o Worker tenta de novo.
5. **Transição só para a frente.** `pago` não volta a `pendente` por evento
   atrasado. A atualização leva a situação esperada no `where`:
   ```sql
   update pagamentos set situacao = 'pago', pago_em = @quando
   where id = @id and situacao = 'pendente';
   ```
   Zero linhas = outro processo já fez, ou o estado mudou. Não é erro.

## Saída: chamada a API externa

- **Sem cabeçalho de idempotência no Pagar.me.** A referência v5 (conferida
  em 21/10/2026, todas as páginas de pedido) não documenta nenhum. Um texto da
  [Stone](https://conteudo.stone.com.br/idempotencia/) cita `Idempotency-key`,
  mas sem respaldo na referência: não usar. O que garante cobrar uma vez é a
  cobrança gravada antes da chamada, com o próprio id como `code`, e a busca
  `GET /orders?code=` antes de qualquer nova tentativa (`Cobrancas.Retomar`).
- **Retentativa só no que é idempotente.** Usar
  [Microsoft.Extensions.Http.Resilience](https://learn.microsoft.com/dotnet/core/resilience/http-resilience)
  (da Microsoft, sobre o [Polly](https://github.com/App-vNext/Polly), 14,3 mil
  estrelas):
  ```csharp
  servicos.AddHttpClient<ClientePagarme>(...)
      .AddStandardResilienceHandler(o => o.Retry.DisableForUnsafeHttpMethods());
  ```
  Sem o `DisableForUnsafeHttpMethods`, o handler padrão repete todos os
  métodos, POST inclusive, e a própria documentação avisa que isso duplica
  registro. Com ele, POST, PATCH, PUT, DELETE e CONNECT não são repetidos.
  POST de cobrança só é repetido pelo nosso código, com a mesma chave de
  idempotência e depois de consultar o pedido.
- **Tempo limite explícito.** Tempo esgotado não quer dizer que falhou. Depois
  dele, consultar antes de repetir.

## Worker

- Pega o trabalho com `for update skip locked`, para duas instâncias não
  pegarem o mesmo evento:
  ```sql
  select id from eventos_externos
  where processado_em is null
  order by recebido_em
  limit 50
  for update skip locked;
  ```
- Define o revendedor antes de cada evento (`RevendedorNaConexao`), um
  revendedor por transação.
- Falha conta tentativas. Depois de N tentativas, o evento fica parado e
  aparece no painel da operadora. Não some da fila em silêncio.

## E-mail e avisos

- Cada envio tem chave própria (por exemplo `recibo:<id da venda>`) com índice
  único. Repetir a rotina não manda o recibo duas vezes.

## Prova

Cada efeito que não pode se repetir tem teste que entrega o mesmo evento duas
vezes e confere um efeito só:

- mesmo webhook duas vezes: uma comissão e um lançamento;
- dois Workers no mesmo evento: um processa, o outro não acha linha;
- evento `pago` seguido de `pendente` atrasado: continua `pago`.
