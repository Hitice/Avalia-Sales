---
name: idempotencia
description: Como o Avalia Sales recebe webhook, chama API externa, aceita POST repetido e roda rotina no Worker sem cobrar, comissionar ou enviar duas vezes. Usar antes de mexer em webhook do Pagar.me ou da D4Sign, no envio ao Resend, em retentativa, em fila do Worker, no middleware Idempotency-Key ou em qualquer efeito que não pode se repetir.
---

# Idempotência no Avalia Sales

Toda entrega de fora chega pelo menos uma vez, e às vezes mais. Webhook chega
repetido, fora de ordem ou atrasado. Requisição de saída cai no meio e não se
sabe se valeu. O cliente reenvia o POST quando a rede falha. A regra é tornar o
efeito repetível sem dano, e não confiar que a repetição não acontece. Não há
biblioteca de idempotência: o Postgres garante unicidade e transação.

## Entrada: webhook (ADR 0005)

1. **Grava antes de processar.** `RotasDeWebhook` só chama
   `CaixaDeEntrada.Registrar`, que insere o evento cru em `eventos_externos`
   com índice único em `(origem, id_externo)`:
   ```sql
   insert into eventos_externos (id, origem, id_externo, tipo, chave, corpo, recebido_em)
   values (...)
   on conflict (origem, id_externo) do nothing;
   ```
   Zero linhas inseridas = repetido. Responde 200 e para.
2. **Responde rápido.** 200 logo depois de gravar; o Worker processa a fila. O
   remetente não fica esperando e não reenvia por tempo esgotado.
3. **Reconsulta a fonte.** `ProcessadorDeEventos` busca o pedido na API de
   origem antes de mudar a situação, fora de transação. O corpo do webhook não é
   fonte de verdade: pode estar velho, fora de ordem ou ser forjado (o Pagar.me
   não assina o webhook; ver a skill `pagarme-checkout-order`).
4. **Efeito e marca na mesma transação.** Pagamento confirmado gera comissão,
   lançamento no razão e `processado_em` do evento numa transação só. Se cair no
   meio, nada fica pela metade e o Worker tenta de novo.
5. **Transição só para a frente.** `Cobranca.Atualizar` (Domain) só aceita
   Pendente para Paga, Cancelada ou Falhou, e Paga para Estornada; devolve se
   mudou. Evento atrasado de `pending` sobre cobrança paga não muda nada e não
   é erro. O evento já está travado (`for update skip locked`, seção Worker),
   então dois processos não aplicam a mesma transição.

## Entrada: POST da própria API

`IdempotenciaDaRequisicao` (middleware único em `Api/Autenticacao`) cobre toda
gravação POST autenticada; a rota nova nasce coberta. Com `Idempotency-Key`: a
primeira requisição executa e a resposta fica gravada em
`chaves_de_idempotencia` por 24 horas (chave por usuário e rota, hash HMAC do
corpo, RLS próprio); a mesma chave com o mesmo corpo recebe a resposta gravada;
outro corpo, 422; execução simultânea, 409. Ficam guardadas só as respostas que
o cliente não vai querer refazer (2xx, 400, 404, 422), sem `Set-Cookie` e até
1 MiB; o resto, e a exceção, soltam a chave. Rota anônima fica de fora. Rota que devolve segredo ou escreve cookie leva
`SemIdempotencia()`. O front manda uma chave por chamada POST.

## Saída: chamada a API externa

- **Cabeçalho `Idempotency-key` no Pagar.me como proteção extra.** O POST de
  `orders` e de `paymentlinks` leva o código da cobrança no cabeçalho (vale 24
  horas no Pagar.me). O que garante cobrar uma vez é a cobrança gravada antes da chamada,
  com o próprio id como `code` (ADR 0004), e a busca `GET /orders?code=` antes
  de qualquer nova tentativa (`Cobrancas.Retomar`, chamado por `Retomadas`).
- **Retentativa só no que é idempotente.** `Microsoft.Extensions.Http.Resilience`
  em `InjecaoDeDependencias`:
  ```csharp
  cliente.AddStandardResilienceHandler(o => o.Retry.DisableForUnsafeHttpMethods());
  ```
  Sem o `DisableForUnsafeHttpMethods`, o handler padrão repete POST e duplica
  registro. POST de cobrança só é repetido pelo nosso código, depois de
  consultar o pedido.
- **Tempo limite.** Pagar.me, D4Sign, Resend, Places, BrasilAPI e Protheus
  usam o padrão do `AddStandardResilienceHandler`; Turnstile e Gotenberg têm
  valor próprio. Tempo esgotado não quer dizer que falhou: consultar antes de
  repetir.
- **Resend recebe `Idempotency-Key`** com a chave do `EmailNaFila`: a
  retentativa não entrega em dobro.

## Worker

- Pega o evento com `for update skip locked` (`ProcessadorDeEventos`), para
  duas instâncias não pegarem o mesmo:
  ```sql
  select id from eventos_externos
  where id = @id and processado_em is null
  for update skip locked;
  ```
- Define o revendedor antes de cada evento (escopo isolado,
  `RevendedorNaConexao`), um revendedor por transação.
- Falha conta tentativas (`EventoExterno.TentativasMaximas`, 10) e guarda o
  último erro. Depois disso o evento fica parado, visível à operadora; não some
  da fila em silêncio.

## E-mail e avisos

- Cada e-mail tem chave própria com índice único (`EmailNaFila.Chave`, como
  `senha:<id do link>` ou `segundo-fator-<mudança>:<usuário>:<segundo>`),
  gravado na mesma transação do fato que o motiva. Repetir a rotina não manda o
  mesmo aviso duas vezes. Até 20 tentativas com espera dobrando (máximo 6 h).

## Prova

Testes que existem:

- mesmo webhook duas vezes: um evento e a cobrança paga uma vez
  (`Webhook_repetido_vira_um_evento_e_a_cobranca_paga_uma_vez`, em
  `Integracoes/FluxoDePagamentoTest.cs`; não conta comissão nem lançamento);
- evento `pago` seguido de `pendente` atrasado: continua `pago`
  (`Evento_atrasado_de_pendente_nao_desfaz_o_pagamento`);
- mesmo POST com a mesma `Idempotency-Key`: uma gravação, segunda resposta com
  `Idempotent-Replayed` (`Api/IdempotenciaTest.cs`).

Sem teste: dois processadores no mesmo evento ao mesmo tempo. A garantia é o
`for update skip locked`; efeito novo que dependa dela merece esse teste.
