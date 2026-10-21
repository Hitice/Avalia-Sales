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
| Integrações | Pagar.me v5, D4Sign v1, Resend, Google Places (New) | - | No código |
| QR da impressão | QRCoder (MIT), SVG e PNG sem System.Drawing | 1.8.0 | No código |
| Resiliência | Microsoft.Extensions.Http.Resilience (Polly) | 10.11.0 | No código |
| Tempo real | SignalR | - | Plano |
| Front | React 19 + Vite, React Router, TanStack Query, Zustand, React Hook Form + Zod, Tailwind 4, shadcn/ui (Radix) | React 19.3, Vite 8.3, Router 8.5, Tailwind 4.3, Zod 4.6, TypeScript 6.0 | Entrada e catálogo no código |
| Mapa, ERP | Google Maps JavaScript, Protheus por simulador | - | Plano |
| Documentação da API | OpenAPI do ASP.NET Core em `/openapi/v1.json` e Swagger UI em `/swagger`, só em Development | OpenApi 10.0.13, Swashbuckle.AspNetCore.SwaggerUI 10.3.0 | No código |
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
| `AvaliaSales.Domain` | Entidades e regras: `Revendedor`, `Produto`, `Cobranca`, `Contrato`, `Pacote`, `PedidoDePlacas`, `Lote`, `PlacaEmitida`, `Placa`, `Vendedor`, `Cliente`, `Venda`, `EmailNaFila`, e as regras puras `CodigoDaPlaca`, `Destino`, `LinkDeAvaliacao`, `Comissao`, `Documentos`, `Telefones` | Depender de outro projeto ou pacote |
| `AvaliaSales.Application` | Casos de uso (`Produtos`, `Pacotes`, `PedidosDePlacas`, `Lotes`, `Estoque`, `FichaDaPlaca`, `Leituras`, `Equipe`, `Vendas`, `Cadastro`, `Revendedores`, `Cobrancas`, `Contratos`, `FilaDeEmails`) e interfaces (`IBancoDeDados`, `IRevendedorAtual`, `IUsuarioAtual`, `IContas`, `IGatewayDePagamento`, `IAssinaturaEletronica`, `IEnvioDeEmail`, `ILugares`, `IModeloDeContrato`) | Conhecer HTTP, Npgsql, Identity ou escrever SQL |
| `AvaliaSales.Infrastructure` | `BancoDeDados` (com as travas de linha em SQL), migrations, Identity e sessões, clientes do Pagar.me, D4Sign, Resend e Places, simulador do Pagar.me, impressão do lote, caixa de entrada, processador, retomadas, envio de e-mails | Ter regra de negócio |
| `AvaliaSales.Api` | Rotas por área (acesso, operadora, revenda, balcão, catálogo, leitura do QR, webhooks), autenticação, políticas por papel, modos de linha de comando | Ter regra de negócio ou SQL |
| `AvaliaSales.Worker` | Volta a cada 10 segundos: caixa de entrada, retomadas, conferência de cobrança pendente, contrato de quem se cadastrou e fila de e-mails | Ter regra de negócio |
| `web` | Front único em React: telas, sessão, chamadas à API | Calcular regra de negócio que a API já decide |

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

- De onde vem o revendedor - na API, só do token (`AcessoDaRequisicao`); no
  Worker, da própria rotina (`RevendedorDaExecucao`), um revendedor por escopo.
  A leitura do QR chega sem login: descobre o dono da placa em
  `placas_emitidas`, abre um escopo novo e assume esse revendedor antes da
  primeira consulta (`AcessoDaRequisicao.Assumir`, que recusa se já houver
  revendedor definido).
- Operadora - não pertence a revendedor (`usuarios.revendedor_id` nulo, com
  check no banco). Vê e altera só as tabelas marcadas com
  `IAtendidoPelaOperadora`, hoje `pedidos_placas`: o filtro do EF ganha
  `OR operadora` e a política de RLS lê `app.operadora = 'sim'`, gravado pela
  mesma conexão. As demais tabelas continuam fechadas a ela.
- Tabelas sem RLS de propósito - `usuarios` e `sessoes` (o login lê antes de
  saber o revendedor), `vinculos_externos` (o webhook chega sem revendedor e é
  ela que diz de quem é), `eventos_externos` e `emails` (não têm revendedor), e
  as tabelas da plataforma `pacotes`, `lotes`, `placas_emitidas` e
  `codigos_reservados` (estoque da Avalia antes do despacho e o código de cada
  placa; o `revendedor_id` de `lotes` e `placas_emitidas` é o destino do
  despacho).
- Testes que guardam - cada trava é testada sozinha, e um teste falha se uma
  tabela com `revendedor_id` nascer sem RLS. Desligar uma trava de propósito
  faz o teste dela falhar; isso foi conferido.

## 3a. Front

- Rotas - `/entrar` e `/painel/catalogo`, este dentro de uma rota protegida
  que manda para a entrada sem sessão válida. Cada área nova entra como rota do
  mesmo app, liberada pelo papel do usuário.
- Sessão - o token fica só em memória (Zustand), nunca em `localStorage` ou
  `sessionStorage`, onde um script injetado o leria. Recarregar a página pede
  login até a API ter o refresh token em cookie HttpOnly. Resposta 401 encerra a
  sessão.
- Chamadas - todas por `chamarApi`, com o prefixo `/api`. Em desenvolvimento o
  Vite repassa `/api` à API local sem o prefixo; em produção o Nginx fará o
  mesmo. A mensagem de erro da tela vem do `detail` do ProblemDetails.
- Dados - TanStack Query para leitura e gravação, com a lista refeita depois de
  gravar. Formulários com React Hook Form e Zod; a API valida de novo.
- Dinheiro - digitado em reais e convertido para centavos inteiros por texto,
  sem ponto flutuante (`paraCentavos`).
- Tema - `web/src/estilos/tokens.css` com as variáveis do shadcn/ui na marca da
  plataforma (noite #120c2b e laser #ee46bc), contraste conferido: texto 17:1,
  secundário 5,8:1, borda de campo 3,4:1, anel de foco 3,2:1. O laser não é
  usado como texto sobre branco (3,4:1). Variantes `dark:` só com a classe
  `.dark`, que ainda não existe.
- Toque - campos e botões com 44 px abaixo de 768 px e 36 px acima.
- Testes - Vitest e Testing Library: conversão de dinheiro, sessão e a tela de
  entrada (validação, senha errada, caminho feliz). 20 testes em 21/10/2026.
  Telas conferidas no navegador a 1440 px e a 390 px, com o revendedor B vendo
  o catálogo vazio.

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

### Fase 1, no banco

| Tabela | Guarda | Regra |
| --- | --- | --- |
| `usuarios` (colunas novas) | `papel` (Operadora, Dono, Vendedor) e `ativo` | Check: operadora sem revendedor, os outros com |
| `sessoes` | Refresh token pelo hash SHA-256, família do login, validade de 14 dias | Sem RLS; hash único |
| `pacotes` | Quantidade e preço oferecidos pela operadora | Sem RLS; quantidade única |
| `lotes` | Número, quantidade, pedido e revendedor do despacho, ativação | Sem RLS; número e pedido únicos |
| `placas_emitidas` | Código (chave), sequência da plataforma, lote, revendedor depois da ativação | Sem RLS; só identificadores |
| `codigos_reservados` | Código que existe fora da plataforma | Sem RLS; o sorteio pula |
| `pedidos_placas` | Pacote copiado (quantidade e valor), situação, pagamento, lote, despacho, ativação | RLS com a operadora |
| `placas` | Placa no estoque do revendedor: custo congelado, situação, consignação, destino, venda, vencimento, leituras | RLS; código único |
| `leituras` | Leituras por placa e dia | RLS; chave (placa, dia), soma por `on conflict` |
| `vendedores` | Equipe, com o mesmo id do login; comissão própria | RLS |
| `clientes` | Negócio que comprou: documento, telefone, e-mail, lugar no Google (só o id) | RLS; documento único por revendedor |
| `vendas` | Placa, cliente, produto, vendedor, valor, tabela, custo e comissão gravados | RLS; uma venda registrada por placa |
| `emails` | Fila de e-mails com chave única, tentativas e id no Resend | Sem RLS |
| `revendedores` (colunas novas) | CNPJ (único), comissão padrão (2000 = 20%), contrato assinado em | - |
| `cobrancas` (coluna nova) | `conferida_em`, para a reconsulta de pendente | Índice por origem |

Se a venda está paga, quem diz é a cobrança mais recente com origem nela, e
não uma coluna da venda: a cobrança gravada antes de o Pagar.me cair já
aponta para a venda.

### Planejado

`regras_comissao` por produto, `lancamentos` (razão, só inserção), `cupons`,
`renovacoes`, `conversas`, `mensagens`, `auditoria`, histórico de destino da
placa. Detalhe no PDD.

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

### Resend (no código)

Conferido na referência oficial em 21/10/2026.

- `POST https://api.resend.com/emails` com `Authorization: Bearer re_...`,
  `from`, `to`, `subject` e `text`. Resposta `{ "id": "..." }`.
- O e-mail entra na tabela `emails` na mesma transação do fato que o motiva,
  com chave única (por exemplo `pedido:{id}:operadora`). O Worker envia com a
  chave no cabeçalho `Idempotency-Key` (até 256 caracteres, vale 24 horas):
  retentativa ou dois Workers não entregam em dobro.
- Sem chave configurada, o Worker não envia e o e-mail espera na fila; nada é
  marcado como enviado sem ter saído.
- E-mails da Fase 1 - pedido novo para a operadora e lote despachado para o
  dono.

### Google Places (no código)

Conferido na referência oficial em 21/10/2026.

- Text Search (New) - `POST https://places.googleapis.com/v1/places:searchText`
  com `X-Goog-Api-Key` e `X-Goog-FieldMask:
  places.id,places.displayName,places.formattedAddress` (SKU Text Search Pro),
  `textQuery`, `languageCode` pt-BR, `regionCode` BR e `pageSize` 8.
- A chave fica no servidor (`Google:ChaveDoServidor`), restrita à Places API e
  ao IP do servidor. O front chama `GET /lugares?texto=`; guarda-se só o id do
  lugar, que pode ser guardado sem prazo.
- Sem chave, a rota responde 422 pedindo o link colado do Perfil da Empresa.

### Simulador do Pagar.me (só Development)

- `Pagarme:Simulado=true` troca o cliente do Pagar.me por `GatewaySimulado`,
  sem rede. A API recusa subir com ele fora de Development.
- Sem estado: o código do pedido é o id da cobrança (UUID v7), que carrega a
  hora da criação. A API e o Worker, processos separados, chegam à mesma
  resposta: pendente nos primeiros 20 segundos e paga depois.
- O pagamento simulado chega pela conferência de cobrança pendente, o mesmo
  caminho que cobre webhook perdido em produção.

### Conferência de cobrança pendente

- Rede de segurança do webhook perdido: o Worker reconsulta pelo código a
  cobrança pendente com pedido, criada há menos de 2 dias, a cada
  `Pagarme:ConferenciaEmSegundos` (300 em produção, 15 no simulador), no máximo
  20 por revendedor a cada volta. Grava `conferida_em`.

### Planejadas

| Serviço | Uso | Como |
| --- | --- | --- |
| Google Maps JavaScript | Mapa dos lugares vendidos | Dado do Places só aparece em mapa do Google; chave do navegador restrita por domínio |
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
| Papéis operadora, dono e vendedor no token, com política por área da API; operadora sem revendedor | No código |
| Refresh token rotativo em cookie HttpOnly e SameSite Strict, 14 dias, só o hash no banco; token já trocado apresentado de novo encerra o login inteiro (RFC 9700) | No código |
| Login desativado não entra e perde as sessões abertas | No código |
| Leitura do QR com limite de 300 por minuto por IP, sem cookie, robôs não contam | No código |
| 2FA do dono, auditoria | Plano |
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
- Rodar sem Docker - a API aceita `--migrar` (com `ConnectionStrings__Dono`),
  `--demonstracao` (só em Development: operadora, pacotes, lotes, duas revendas,
  pedidos em cada etapa, equipe, vendas pagas e pendentes e leituras, tudo
  simulado), `--novo-revendedor --nome= --email= --senha=` e `--nova-operadora
  --nome= --email= --senha=`. Passo a passo no README.
- Desenvolvimento local - Postgres 17 do Homebrew; os testes criam e apagam
  o próprio banco. No VPS o banco nasce vazio e as mesmas migrations criam as
  tabelas: não há dado a migrar da máquina.

## 10. CI e qualidade

- CI no GitHub Actions - restore, build em Release e a suíte inteira contra um
  Postgres 17 de serviço, a cada push no `main` e em pull request. O front tem
  um job próprio no Node 24 LTS: `npm ci`, tipos, lint (oxlint), testes e build.
- Build trata aviso como erro, com `AnalysisMode=Recommended` e as regras de
  código sem uso (IDE0051, IDE0052, IDE0060, IDE0005) ligadas no
  `.editorconfig`.
- Testes - xUnit v3 na Microsoft.Testing.Platform. Unidade (divisão, regras de
  cobrança, contrato, código da placa, destino, link de avaliação, ciclo da
  placa, lote, venda, comissão, documentos), contrato HTTP dos clientes contra
  servidor falso, e fluxo completo no Postgres (lote do pacote à ativação,
  venda, leitura do QR, consignação, equipe, sessão e cadastro). O Pagar.me da
  API de teste é falso: nenhuma chamada sai para a rede. 134 testes em
  21/10/2026.
- Os testes entram no Postgres com o papel `avalia_app_teste`, membro do
  `avalia_app`: as mesmas permissões e o mesmo RLS, sem trocar a senha que a API
  local usa.
- Teste de trava de segurança é conferido desligando a trava e vendo o teste
  falhar.
- Plano - SonarCloud no pull request, Sentry na API e no Worker, deploy no VPS
  pelo runner próprio com aprovação manual.

## 11. Decisões técnicas

Resumo. Contexto, opções e consequências de cada uma ficam nos ADRs, em
[docs/adr](adr/README.md), no formato MADR.

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
| 21/10/2026 | Token do front só em memória | Script injetado não lê memória do app; o refresh em cookie HttpOnly resolve a recarga |
| 21/10/2026 | Papel da operadora sem revendedor e tabela marcada `IAtendidoPelaOperadora` | A operadora atende os pedidos de todos sem abrir as demais tabelas a ela |
| 21/10/2026 | `placas_emitidas` da plataforma, sem RLS, e `placas` do revendedor | A leitura do QR descobre o dono sem login; o destino e a venda ficam isolados |
| 21/10/2026 | Refresh token rotativo em cookie HttpOnly | O token de acesso continua só em memória, e recarregar a página não derruba a sessão |
| 21/10/2026 | Pagamento da venda lido da cobrança por origem | A cobrança gravada antes da queda do Pagar.me já aponta para a venda |
| 21/10/2026 | Simulador do Pagar.me sem estado, pelo UUID v7 | API e Worker são processos separados e precisam da mesma resposta |
| 21/10/2026 | Swagger UI sobre o OpenAPI nativo do ASP.NET Core | O documento é o do framework; o Swashbuckle entra só como página |
| 21/10/2026 | `@hookform/resolvers` 5.9 e não 6.0 | A 6.0.0 declara versões conflitantes de uma dependência opcional e não instala sem forçar |
| 21/10/2026 | Nenhuma biblioteca de idempotência pronta | A mais citada tinha 329 estrelas e nenhum commit desde 11/2024; o Postgres já garante unicidade |

## 12. Pendências técnicas

- Rodar o fluxo nos sandboxes do Pagar.me e da D4Sign com chave real e
  confirmar os pontos marcados "a conferir".
- Instalar Docker na máquina de desenvolvimento e rodar o Compose.
- Cartão na venda pede a tokenização do Pagar.me no front (chave pública);
  até lá a venda sai em Pix.
- Cadastro de recebedor pela API do Pagar.me (`POST /recipients`); até lá a
  operadora informa o `rp_` do revendedor.
- Link de pagamento do Pagar.me (`POST /paymentlinks`) entra com o pedido de
  placas.
- Pacote do front com 511 kB (161 kB comprimido): dividir por rota quando a
  área do vendedor entrar, para o celular carregar só o que usa.
- Storybook e axe, pedidos pela skill `design-system`, ainda não instalados;
  entram com o primeiro componente próprio.
- Nginx com `/api/`, `/q/` e `/webhooks/` para a API e o resto para o front.
- Histórico de destino da placa (o Sales guarda a linha do tempo) e auditoria
  das ações do dono e da operadora.

## 13. Registro de mudanças

| Data | Mudança |
| --- | --- |
| 21/10/2026 | Fase 1 na API - tabelas da plataforma e do revendedor, papéis e políticas, sessão com refresh em cookie, rotas por área, leitura do QR, impressão do lote, Resend, Places, simulador do Pagar.me, conferência de cobrança pendente, contrato automático e demonstração completa. Testes com papel próprio no Postgres. Página de rotas trocada do Scalar pelo Swagger UI. |
| 21/10/2026 | Comando `--novo-revendedor` para a operadora cadastrar revendedor e dono pela linha de comando. |
| 21/10/2026 | Front em `web/`: entrada, painel protegido e catálogo, tema da plataforma com contraste conferido, toque de 44 px no celular, 20 testes e job no CI. |
| 21/10/2026 | Página de rotas (OpenAPI e Scalar em `/rotas`) e comando `--demonstracao`, os dois só em Development. |
| 21/10/2026 | Documento criado. Fase 0 (camadas, login, isolamento, CI) e integrações do Pagar.me e da D4Sign com caixa de entrada, Worker e retomadas. |
