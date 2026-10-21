# Avalia Sales - documento de software (SDD)

Como a plataforma é construída. O que ela faz e as regras de negócio estão no
[PDD](PDD.md). Os dois são atualizados no mesmo commit da mudança que os
afeta, com uma linha no registro de mudanças do fim.

Cada seção diz o que já está no código e o que ainda é plano.

## 1. Stack

| Camada | Tecnologia | Versão | Situação |
| --- | --- | --- | --- |
| API | C#, ASP.NET Core Web API (minimal APIs) | .NET 10 LTS | No código |
| Jobs | Worker Service | .NET 10 LTS | No código |
| Banco | PostgreSQL com EF Core e Npgsql | Postgres 17, EF Core 10.0.13, Npgsql 10.0.3 | No código |
| Acesso | ASP.NET Identity e JWT (HMAC-SHA256) | 10.0.13 | No código |
| Integrações | Pagar.me v5, D4Sign v1 | - | No código |
| Resiliência | Microsoft.Extensions.Http.Resilience (Polly) | 10.11.0 | No código |
| Tempo real | SignalR | - | Plano |
| Front | React 19 + Vite, React Router, TanStack Query, Zustand, React Hook Form + Zod, Tailwind 4, shadcn/ui | - | Plano |
| E-mail, mapa, ERP | Resend, Google Maps e Places (New), Protheus por simulador | - | Plano |
| Documentação da API | OpenAPI do ASP.NET Core e página Scalar em `/rotas`, só em Development | OpenApi 10.0.13, Scalar 2.18.2 | No código |
| Infra | Linux, Docker Compose, Nginx, Let's Encrypt | - | Compose e Dockerfile no repositório |
| CI | GitHub Actions, self-hosted runner no deploy, SonarCloud, Sentry | - | CI de build e teste no repositório |

A versão do .NET e a de cada pacote moram só em `Directory.Build.props` e
`Directory.Packages.props`.

## 2. Arquitetura

```
Front (React) ──┐
Página do QR ───┼── Nginx ── Api ──┬── PostgreSQL
                │                  ├── Pagar.me, D4Sign, Resend, Google
Webhooks ───────┘   Worker ────────┘
```

| Projeto | Responsabilidade | Não pode |
| --- | --- | --- |
| `AvaliaSales.Domain` | Entidades e regras (`Revendedor`, `Produto`, `Cobranca`, `Contrato`, `VinculoExterno`, `DivisaoDaCobranca`) | Depender de outro projeto ou pacote |
| `AvaliaSales.Application` | Casos de uso (`Produtos`, `Cobrancas`, `Contratos`) e interfaces (`IBancoDeDados`, `IRevendedorAtual`, `IGatewayDePagamento`, `IAssinaturaEletronica`) | Conhecer HTTP, Npgsql ou Identity |
| `AvaliaSales.Infrastructure` | `BancoDeDados`, migrations, Identity, clientes do Pagar.me e da D4Sign, caixa de entrada, processador | Ter regra de negócio |
| `AvaliaSales.Api` | Rotas, autenticação, webhooks, saúde, modo `--migrar` | Ter regra de negócio ou SQL |
| `AvaliaSales.Worker` | Volta a cada 10 segundos: caixa de entrada e retomadas | Ter regra de negócio |

Regra recusada sai do Domain como `RegraDeNegocioException` e a API responde
422. Falha de sistema externo vira `IntegracaoException`, sem chave nem URL na
mensagem.

## 3. Isolamento entre revendedores

O revendedor é o tenant. Um banco só, com `revendedor_id` em toda tabela de
negócio e duas travas independentes:

1. Filtro global do EF Core - toda entidade que implementa `IDoRevendedor`
   ganha o filtro sozinha, montado por expressão em `BancoDeDados`. O
   revendedor de um registro novo sai sempre da requisição e não muda depois.
2. RLS no PostgreSQL - `RevendedorNaConexao` grava `app.revendedor_id` na
   sessão a cada conexão aberta, e a política de cada tabela lê esse valor.
   `FORCE ROW LEVEL SECURITY` vale também para o dono. A API conecta com o papel
   `avalia_app`, sem superusuário e sem `BYPASSRLS`; quem migra é o dono do
   banco.

- De onde vem o revendedor - na API, só do token (`RevendedorDoToken`); no
  Worker, da própria rotina (`RevendedorDaExecucao`), um revendedor por escopo.
- Tabelas sem RLS de propósito - `usuarios` (o login lê antes de saber o
  revendedor), `vinculos_externos` (o webhook chega sem revendedor e é ela que
  diz de quem é) e `eventos_externos` (não tem revendedor).
- Testes que guardam - cada trava é testada sozinha, e um teste falha se uma
  tabela com `revendedor_id` nascer sem RLS. Desligar uma trava de propósito
  faz o teste dela falhar; isso foi conferido.

## 4. Modelo de dados

Dinheiro em centavos (`bigint`, colunas `_cents`), taxa em pontos base
(`_bps`). Datas em UTC (`timestamptz`). Situações gravadas como texto.

### No banco hoje

| Tabela | Guarda | Regra |
| --- | --- | --- |
| `revendedores` | Nome, taxa da Avalia (`taxa_avalia_bps`, padrão 199), recebedor no Pagar.me | Sem RLS: é a lista que o Worker percorre |
| `usuarios`, `papeis` e demais do Identity | Login, com o revendedor do usuário | E-mail único na plataforma |
| `produtos` | Tabela de preço do revendedor | RLS |
| `cobrancas` | Cobrança ao cliente final: origem, meio, valor, taxa da Avalia, situação, pedido no Pagar.me, Pix | RLS; `pedido_no_gateway` único; taxa copiada na criação |
| `contratos` | PDF, hash SHA-256, signatário, documento na D4Sign, etapa do envio, situação | RLS; `documento_na_assinatura` único |
| `vinculos_externos` | Código no Pagar.me ou documento na D4Sign, e o revendedor dono | Chave (origem, id_externo); só identificadores |
| `eventos_externos` | Webhook cru, chave do vínculo, tentativas, último erro, processado em | Único (origem, id_externo) |

### Planejado

`pedidos_placas`, `lotes`, `placas`, `clientes`, `negocios`, `vendas`,
`regras_comissao`, `lancamentos` (razão, só inserção), `leituras`
(particionada por mês), `cupons`, `conversas`, `mensagens`, `auditoria`.
Detalhe no PDD.

Migration só aditiva. Tabela nova com `revendedor_id` chama
`IsolarPorRevendedor` no fim do `Up`.

## 5. Integrações

Cada integração mora atrás de uma interface na Application, com a
implementação na Infrastructure. Retentativa automática só em método seguro:
`AddStandardResilienceHandler(o => o.Retry.DisableForUnsafeHttpMethods())`,
porque o padrão do handler repete também POST, e um POST de cobrança repetido
cobra duas vezes. Um teste falha se essa opção sair.

### Pagar.me (no código)

Conferido na referência oficial em 21/10/2026.

- Endereço e chave - `https://api.pagar.me/core/v5/` em teste e produção; a
  chave secreta define o ambiente (`sk_test_` ou `sk_`). Basic auth com a chave
  como usuário e senha vazia.
- Criar pedido - `POST /orders` com `code` igual ao id da cobrança (32
  caracteres), um item, o cliente, e o pagamento em Pix (`expires_in` em
  segundos) ou cartão (`card_token` gerado no navegador; o número do cartão
  nunca passa pela API).
- Split - duas regras `flat` em centavos: revendedor e Avalia. O tipo
  `percentage` não aceita 1,99. O revendedor tem `liable`,
  `charge_processing_fee` e `charge_remainder_fee`. Regra de valor zero não é
  enviada. Split só existe para conta PSP.
- Sem cabeçalho de idempotência - a referência não documenta nenhum. A
  cobrança é gravada antes da chamada, e quando a chamada cai o pedido é
  procurado por `GET /orders?code=` antes de qualquer nova tentativa.
- Situação - lida do pedido (`paid`, `canceled`, `failed`, `pending`);
  estorno e chargeback lidos da cobrança (`refunded`, `chargedback`).
- Webhook - `POST /webhooks/pagarme/{segredo}`. O Pagar.me não assina o
  webhook: o segredo na URL barra quem não a conhece (resposta 404), e o
  Worker reconsulta o pedido antes de mudar qualquer coisa. A chave do evento é
  o `id` do hook; o vínculo é achado pelo `data.code`.
- A conferir no sandbox - se a cobrança repete o `code` do pedido em todos os
  eventos (os exemplos da referência mostram que sim) e como o painel do
  Pagar.me deixa configurar o webhook.

### D4Sign (no código)

Conferido na referência oficial em 21/10/2026.

- Endereço e chave - `https://sandbox.d4sign.com.br/api/v1/` por padrão;
  produção (`secure.d4sign.com.br`) só por configuração. `tokenAPI` e
  `cryptKey` vão na URL; por isso a URL nunca entra em erro nem em log
  (`System.Net.Http.HttpClient` em Warning).
- Envio em quatro passos - `uploadbinary` (PDF em base64 no cofre),
  `webhooks`, `createlist` (signatário, `act` 1 = assinar) e `sendtosigner`.
  Cada passo é gravado ao terminar; a retomada continua do último, sem repetir
  o upload.
- Limite - a conta padrão aceita 10 requisições por hora, e um envio gasta
  quatro. O Worker retoma no máximo 2 contratos por volta. Para volume real,
  pedir aumento à D4Sign.
- Situação - `GET /documents/{uuid}`: `statusId` 4 é finalizado, 6 é
  cancelado; os demais são andamento.
- Webhook - `POST /webhooks/d4sign`, em formulário (`uuid`, `type_post`,
  `email`). O cabeçalho `Content-Hmac: sha256=<hex>` é o HMAC-SHA256 do UUID do
  documento com a secret key MAC da conta, comparado em tempo constante. O HMAC
  cobre só o UUID; o Worker reconsulta a situação. A chave do evento é
  `uuid:type_post:email`.

### Planejadas

| Serviço | Uso | Como |
| --- | --- | --- |
| Resend | Pedido para a Avalia, convite, recibo, vencimento | Fila no Worker, chave única por envio |
| Google Places (New) | Achar o negócio na venda | Autocomplete com token de sessão; guarda só o `place_id` |
| Google Maps JavaScript | Mapa dos lugares vendidos | Dado do Places só aparece em mapa do Google |
| SignalR | Chat e avisos | Hub com JWT, grupo por revendedor |
| Protheus | Pedido de placas pago vira pedido de venda | Interface com simulador da API REST |

## 6. Ambientes de teste das integrações

Conferido nas páginas oficiais em 21/10/2026.

| Serviço | Ambiente de teste | Limites e ressalvas |
| --- | --- | --- |
| Pagar.me | Chaves `sk_test_` e `pk_test_` no mesmo endereço da produção | Pix simulado aprova até R$ 500,00 e falha acima; o simulador de Pix não funciona com split, então o teste de split usa cartão (`4000000000000010` aprova, `4000000000000028` recusa). Split exige conta PSP: confirmar com o comercial se a conta de teste tem split habilitado |
| D4Sign | `sandbox.d4sign.com.br`, cadastro próprio | Sem validade jurídica; mesmo limite de 10 requisições por hora até a D4Sign aumentar |
| Resend | Endereços de teste `delivered@resend.dev`, `bounced@resend.dev`, `complained@resend.dev` | Plano gratuito com 3.000 e-mails por mês e 100 por dia; o teste conta na cota |
| Google Maps e Places | Sem sandbox: a chamada é real | Cota gratuita por SKU por mês (10.000 carregamentos de mapa dinâmico, 10.000 Place Details Essentials); exige conta de faturamento no Google Cloud |
| Protheus | Sem ambiente público | Simulador próprio em homologação, identificado como tal |
| Sentry | Plano Developer gratuito | 1 usuário, 5 mil erros, 30 dias de histórico |
| SonarCloud | Gratuito para repositório público | Privado: gratuito até 50 mil linhas |

## 7. Webhooks e Worker

1. A rota grava o evento cru em `eventos_externos` com
   `insert ... on conflict do nothing` e responde 200. Reenvio do mesmo evento
   não grava nada.
2. O Worker lê os pendentes, acha o revendedor pelo vínculo e reconsulta a
   fonte fora de transação.
3. Abre um escopo já isolado no revendedor, trava o evento com
   `for update skip locked` e aplica o efeito e a marca de processado na mesma
   transação. Dois Workers no mesmo evento: um aplica, o outro não acha linha.
4. Falha soma tentativa e guarda o erro; depois de 10 tentativas o evento fica
   parado. Evento de pedido ou documento que não é nosso é encerrado com nota.

Retomadas, revendedor por revendedor - cobrança pendente sem pedido há mais
de 2 minutos é procurada pelo código, e vira falha depois de 15 minutos sem
pedido; contrato com envio incompleto retoma do último passo, até 5
tentativas.

## 8. Segurança e LGPD

| Ponto | Situação |
| --- | --- |
| Token de 15 minutos, emissor, público e algoritmo validados, chave de 32+ caracteres do ambiente | No código |
| Login com resposta igual para e-mail inexistente e senha errada, bloqueio após 5 erros, 10 tentativas por minuto por IP | No código |
| Senha por tamanho (10+), sem regra de tipo de caractere | No código |
| Webhooks com limite de 120 por minuto por IP e corpo de até 1 MB | No código |
| Segredos só no ambiente; configuração das integrações validada na partida | No código |
| Refresh token rotativo em cookie HttpOnly, 2FA do dono, auditoria | Plano |
| LGPD - revendedor controlador, plataforma operadora; dado mínimo; retenção; anonimização | Plano, detalhe no PDD |

Revisão pela skill `seguranca` (OWASP ASVS 5.0).

## 9. Infraestrutura

- Dockerfile - uma compilação e duas imagens: `api` (que também roda
  `--migrar` como dono do banco) e `worker`. As duas rodam sem root.
- Docker Compose de desenvolvimento - `postgres` (17, papel `avalia_app` criado
  na primeira subida), `migrar` (termina antes das outras), `api` e `worker`.
  A configuração das integrações é obrigatória: sem ela, API e Worker não
  sobem. Segredos no `.env`, modelo em `.env.example`. Ainda não rodado nesta
  máquina, que não tem Docker.
- Produção - VPS Linux com Compose, Nginx e Let's Encrypt, um domínio para
  todos os revendedores.
- Rodar sem Docker - a API aceita `--migrar` (com `ConnectionStrings__Dono`) e
  `--demonstracao`, que cria dois revendedores fictícios com login, só em
  Development. Passo a passo no README.
- Desenvolvimento local - Postgres 17 do Homebrew; os testes criam e apagam
  o próprio banco. No VPS o banco nasce vazio e as mesmas migrations criam as
  tabelas: não há dado a migrar da máquina.

## 10. CI e qualidade

- CI no GitHub Actions - restore, build em Release e a suíte inteira contra um
  Postgres 17 de serviço, a cada push no `main` e em pull request.
- Build trata aviso como erro, com `AnalysisMode=Recommended` e as regras de
  código sem uso (IDE0051, IDE0052, IDE0060, IDE0005) ligadas no
  `.editorconfig`.
- Testes - xUnit v3 na Microsoft.Testing.Platform. Unidade (divisão, regras de
  cobrança e contrato, HMAC), contrato HTTP dos clientes contra servidor falso,
  e fluxo completo no Postgres (webhook pela API, Worker aplicando). 55 testes
  em 21/10/2026.
- Teste de trava de segurança é conferido desligando a trava e vendo o teste
  falhar.
- Plano - SonarCloud no pull request, Sentry na API e no Worker, deploy no VPS
  pelo runner próprio com aprovação manual.

## 11. Decisões técnicas

| Data | Decisão | Motivo |
| --- | --- | --- |
| 21/10/2026 | .NET 10 LTS | O .NET 8 sai de suporte em 10/11/2026; o código é o mesmo |
| 21/10/2026 | Tenant no banco único com filtro do EF Core e RLS | Duas travas independentes contra vazamento entre revendedores |
| 21/10/2026 | Front único em React | Um design system, um build, um conjunto de componentes |
| 21/10/2026 | Split em `flat` calculado por nós | `percentage` não aceita 1,99 |
| 21/10/2026 | Cobrança gravada antes de chamar o Pagar.me, com o id como `code` | A API não documenta idempotência; o código acha o pedido depois de uma queda |
| 21/10/2026 | Vendedor não é recebedor no Pagar.me | O revendedor paga a comissão por Pix; a plataforma calcula o saldo |
| 21/10/2026 | Webhook só grava; o Worker reconsulta a fonte | Nenhum dos dois webhooks assina o corpo |
| 21/10/2026 | Vínculo externo sem RLS | O webhook chega sem revendedor |
| 21/10/2026 | Nenhuma biblioteca de idempotência pronta | A mais citada tinha 329 estrelas e nenhum commit desde 11/2024; o Postgres já garante unicidade |

## 12. Pendências técnicas

- Rodar o fluxo nos sandboxes do Pagar.me e da D4Sign com chave real e
  confirmar os pontos marcados "a conferir".
- Instalar Docker na máquina de desenvolvimento e rodar o Compose.
- Endpoint de criação de cobrança e de contrato entra com as telas da Fase 1;
  hoje só os casos de uso e os testes os chamam.
- Link de pagamento do Pagar.me (`POST /paymentlinks`) entra com o pedido de
  placas.
- Testes e API local usam o mesmo papel `avalia_app` no Postgres da máquina, e a
  suíte troca a senha dele a cada execução: depois de rodar os testes, a API
  local precisa da senha de volta (README). Resolver com um nome de papel
  configurável.

## 13. Registro de mudanças

| Data | Mudança |
| --- | --- |
| 21/10/2026 | Página de rotas (OpenAPI e Scalar em `/rotas`) e comando `--demonstracao`, os dois só em Development. |
| 21/10/2026 | Documento criado. Fase 0 (camadas, login, isolamento, CI) e integrações do Pagar.me e da D4Sign com caixa de entrada, Worker e retomadas. |
