---
name: pagarme-checkout-order
description: Pagar.me v5 no Avalia Sales - POST /paymentlinks (type order) no pedido de placas, POST /orders na venda e na renovação, estorno, reconsulta e webhook. Usar antes de qualquer chamada à API do Pagar.me.
---

# Pagar.me: checkout de cobrança pontual

Skill oficial do Pagar.me, baixada de
https://docs.pagar.me/docs/checkout_pagarme_skill_order.md em 2026-10-21
(updatedAt 2026-05-20). O conteúdo original está abaixo, sem alteração; ele
traz exemplos em Next.js e Python, User-Agent e validação de assinatura que o
projeto não usa. As notas desta seção valem sobre ele.

## Notas do Avalia Sales

Conferidas na referência oficial em 2026-10-21. O que o código faz hoje:

- **Onde.** `src/AvaliaSales.Infrastructure/Integracoes/Pagarme`
  (`ClientePagarme`, `OpcoesPagarme`), chave em `Pagarme__ChaveSecreta`,
  segredo do webhook em `Pagarme__SegredoDoWebhook`, recebedor da plataforma em
  `Pagarme__RecebedorAvalia`, chave pública do cartão em
  `Pagarme__ChavePublica` (vazia, a venda é só Pix). Em Development, com
  `Pagarme__Simulado=true` (`appsettings.Development.json`), roda o simulador
  sem estado (ADR 0009).
- **Venda e renovação** usam `POST /orders` com Pix (a renovação só Pix) ou
  cartão tokenizado no navegador, split
  em centavos (`flat`, ADR 0003) e a cobrança gravada antes da chamada, com o
  próprio id como `code` (ADR 0004). `GET /orders?code=` reconsulta;
  `DELETE /charges/{id}` estorna, total ou parcial com `amount`.
- **Pedido de placas** usa `POST /paymentlinks` com `type: order`,
  `max_paid_sessions: 1` e sem split (ADR 0012): o conteúdo original abaixo
  vale para esse caso. O cliente (`ClientePagarme.MontarLink`) manda também
  `order_code` igual ao id da cobrança (liga o link ao `GET /orders?code=`,
  ADR 0004), `expires_in` em minutos (3 dias, `Cobrancas.ValidadeDoLink`),
  `pix_settings.expires_in` em segundos, `boleto_settings.due_in` 3, uma
  parcela no cartão e `name` com até 64 caracteres.
- **Cancelar o link** - `PATCH /paymentlinks/{payment_link_id}/cancel`, sem
  corpo, responde 204 (docs.pagar.me/reference/cancelar-um-link-de-pagamento).
  O pedido (`or_`) só existe depois do pagamento: o link não pago é cancelado
  por aqui, o pago pelo `DELETE /charges/{id}`.
- **Webhooks do conteúdo original** - o tipo do evento não decide nada aqui:
  o Worker reconsulta pelo código em todo evento.
- **Base URL.** `OpcoesPagarme.UrlBase` tem `https://api.pagar.me/core/v5/`
  como padrão para teste e produção; a chave define o ambiente. A skill cita
  `https://sdx-api.pagar.me/core/v5` para teste; não usar sem um teste com chave
  real confirmar. Fonte: https://docs.pagar.me/reference/autentica%C3%A7%C3%A3o-2
- **Prefixo da chave de produção.** A referência mostra `sk_`, e não
  `sk_live_`. Não validar a chave pelo prefixo.
- **Split no link só com cartão.** `split_settings` no link de pagamento só é
  repassado quando o link é pago com `credit_card` ou carteira digital. Pix e
  boleto pagos pelo link não dividem. Por isso a venda com split vai por
  `POST /orders`. Fonte: https://docs.pagar.me/reference/criar-link
- **Link de assinatura.** `type: subscription` aceita só cartão e não faz
  split. Não usar no projeto.
- **Header `User-Agent: pagarme-skill-generated/1.0`.** Só identifica código
  gerado pela skill. Não é exigido pela referência da API. O cliente HTTP usa
  um User-Agent próprio do Avalia Sales.
- **Assinatura do webhook.** A skill manda validar a assinatura, mas a página
  de webhooks não documenta assinatura. O projeto põe o segredo na URL do
  webhook (`/webhooks/pagarme/{segredo}`, 404 sem ele), grava o evento cru com
  chave única e reconsulta a API antes de processar (skill `idempotencia`).
- **Retentativa.** O cliente HTTP não repete POST (`DisableForUnsafeHttpMethods`).
  O POST de `orders` e de `paymentlinks` leva `Idempotency-key` com o código da
  cobrança (o Pagar.me guarda a chave por 24 horas, segundo a Stone); é proteção
  extra, porque a garantia continua sendo a cobrança gravada antes (ADR 0004).
- **Exemplos em Next.js e Python.** Servem só como referência do payload.
- **Dinheiro.** O `amount` em centavos bate com as colunas `_cents` do
  projeto. Não converter de decimal.

## Conteúdo original

# Pagar.me Checkout — SKILL Cobrança Pontual

## Conceito
Checkout para transação única: `type: "order"`.
Merchant cria o link → cliente acessa a URL → paga → pedido encerrado.
Sem formulário próprio, sem lidar com dados de cartão.

## Pré-requisitos
- Conta Pagar.me ativa
- Secret key: `sk_...` (dashboard)
- Ambientes:

| Ambiente | Base URL | Credencial |
|---|---|---|
| Teste | `https://sdx-api.pagar.me/core/v5` | `sk_test_...` |
| Produção | `https://api.pagar.me/core/v5` | `sk_live_...` |

- Variáveis de ambiente necessárias:
  - `PAGARME_SECRET_KEY`: sua secret key
  - `PAGARME_BASE_URL`: URL do ambiente (teste ou produção)

## Autenticação
HTTP Basic Auth — secret key como usuário, senha vazia:
```
Authorization: Basic base64("sk_SUA_CHAVE:")
```

## Header obrigatório
Toda requisição deve incluir:
```
User-Agent: pagarme-skill-generated/1.0
```

## Criar Checkout Pontual
`POST /paymentlinks`

**Campos obrigatórios:**

| Campo | Tipo | Descrição |
|---|---|---|
| `type` | string | `"order"` |
| `payment_settings` | object | Métodos aceitos |
| `cart_settings.items[]` | array | Itens do carrinho |
| `cart_settings.items[].name` | string | Nome do produto |
| `cart_settings.items[].amount` | int32 | Valor em centavos |
| `cart_settings.items[].default_quantity` | int32 | Quantidade |

> ⚠️ Valores em **centavos**: R$100,00 = `10000`

### ⚡ Configurações Avançadas de Link (Controle de Escassez e Validade)

Para cenários de promoções por tempo limitado, controle de estoque ou campanhas futuras, a API do Pagar.me permite controlar o ciclo de vida do link de pagamento. Se o usuário solicitar regras de expiração ou limite de vendas, adicione os seguintes parâmetros na raiz do payload do `order`:

* **`max_paid_sessions`** (integer): Limita o número de vendas aprovadas originadas por este link. Essencial para controle de escassez (ex: "Apenas 50 ingressos disponíveis"). Quando o limite é atingido, o link é desativado automaticamente.
* **`expires_in`** (integer): Define a validade do link em minutos a partir do momento de sua criação (ex: `60` para um link válido por exatamente 1 hora).
* **`expires_at`** (string/datetime): Define uma data e hora exatas e fixas para a expiração do link (ex: `2026-12-31T23:59:59Z`). 
  * ⚠️ **Atenção:** Ensine a integração a enviar `expires_in` **OU** `expires_at`. Nunca envie ambos no mesmo payload.
* **`is_building`** (boolean): Se definido como `true`, o link é criado em modo inativo (rascunho). O cliente final não conseguirá pagar até que o lojista o ative no painel. O padrão é `false`.

Referência completa de parâmetros: https://docs.pagar.me/reference/criar-link.md

```json
{
  "type": "order",
  "payment_settings": {
    "accepted_payment_methods": ["credit_card", "pix", "boleto"]
  },
  "cart_settings": {
    "items": [
      {
        "name": "Produto",
        "amount": 10000,
        "description": "Produto",
        "default_quantity": 1
      }
    ]
  }
}
```

**Resposta — salve a URL e redirecione o comprador:**
```json
{
  "id": "pl_...",
  "url": "https://checkout.pagar.me/...",
  "status": "active"
}
```

Referência completa de resposta: https://docs.pagar.me/reference/checkout-response.md

## Exemplo — Next.js
```js
const response = await fetch(`${process.env.PAGARME_BASE_URL}/paymentlinks`, {
  method: 'POST',
  headers: {
    'Authorization': 'Basic ' + btoa(process.env.PAGARME_SECRET_KEY + ':'),
    'Content-Type': 'application/json',
    'User-Agent': 'pagarme-skill-generated/1.0'
  },
  body: JSON.stringify({
    type: 'order',
    payment_settings: { accepted_payment_methods: ['credit_card', 'pix', 'boleto'] },
    cart_settings: { items: [{ name: 'Produto', amount: 10000, description: 'Produto', default_quantity: 1 }] }
  })
});
const { url } = await response.json();
// redirecionar o comprador para `url`
```

## Exemplo — Python
```python
import requests, base64, os

credentials = base64.b64encode(f"{os.environ['PAGARME_SECRET_KEY']}:".encode()).decode()

response = requests.post(
    f"{os.environ['PAGARME_BASE_URL']}/paymentlinks",
    headers={
        'Authorization': f'Basic {credentials}',
        'Content-Type': 'application/json',
        'User-Agent': 'pagarme-skill-generated/1.0'
    },
    json={
        'type': 'order',
        'payment_settings': {'accepted_payment_methods': ['credit_card', 'pix', 'boleto']},
        'cart_settings': {'items': [{'name': 'Produto', 'amount': 10000, 'description': 'Produto', 'default_quantity': 1}]}
    }
)
url = response.json()['url']
# redirecionar o comprador para `url`
```

## Meios de pagamento

| Método | Campo | Liquidação |
|---|---|---|
| Cartão de crédito | `credit_card` | D+30 (padrão) |
| Pix | `pix` | Instantâneo |
| Boleto | `boleto` | 3 dias úteis |

## Webhooks

| Evento | Ação |
|---|---|
| `order.paid` | Liberar pedido |
| `order.payment_failed` | Notificar recusa |
| `charge.refunded` | Processar reembolso |
| `checkout.canceled` | Link expirado |

## Dados de teste

| Cartão | Resultado |
|---|---|
| `4000000000000010` | Aprovado |
| `4000000000000028` | Recusado |

- CVV: qualquer 3 dígitos — Validade: qualquer data futura
- Chave Pix de teste: disponível no dashboard

## Erros comuns

| Erro | Causa | Solução |
|---|---|---|
| `type is required` | Campo `type` ausente | Adicionar `"type": "order"` |
| Valor incorreto | Enviou em reais | Converter para centavos |
| `cart_settings missing` | Sem itens | Adicionar `cart_settings.items` |
| Credencial inválida | Chave errada | Verificar `sk_...` no dashboard |

## Checklist de produção
- [ ] Trocar `PAGARME_BASE_URL` de teste para produção
- [ ] Trocar chave de teste pela chave de produção
- [ ] Configurar endpoint de webhook com HTTPS
- [ ] Validar assinatura do webhook
- [ ] Testar todos os meios de pagamento
- [ ] Armazenar `payment_link.id` no banco de dados
- [ ] Definir `expires_at` ou `max_paid_sessions` se necessário

