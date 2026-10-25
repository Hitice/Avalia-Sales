# Avalia Sales - documento de software (SDD)

Como a plataforma é construída. O que ela faz e as regras de negócio estão no
[PDD](PDD.md). Toda mudança de tabela, integração, configuração ou decisão
técnica atualiza este documento no mesmo commit, com uma linha no registro de
mudanças do fim.

Em produção em sales.avaliaone.com.br. Onde algo depende de conta ou chave
externa, a seção diz.

Termos - a revenda é o tenant, com a entidade `Revendedor` e a coluna
`revendedor_id`. O papel do usuário dono da revenda é `Dono` no código e
aparece como Revendedor na interface.

## 1. Stack

| Camada | Tecnologia | Versão | Situação |
| --- | --- | --- | --- |
| API | C#, ASP.NET Core Web API (minimal APIs) | .NET 10 LTS | Em produção |
| Jobs | Worker Service | .NET 10 LTS | Em produção |
| Banco | PostgreSQL com EF Core e Npgsql | Postgres 17, EF Core 10.0.13, Npgsql 10.0.3 | Em produção |
| Acesso | ASP.NET Identity e JWT (HMAC-SHA256) | 10.0.13 | Em produção |
| Integrações | Pagar.me v5, D4Sign v1, Resend, Google Places (New), BrasilAPI (CNPJ e CEP), Gotenberg 8 (PDF) | Gotenberg 8.38.1 | Em produção; a D4Sign no ambiente de testes |
| ERP | Protheus por contrato REST próprio | - | Conexão pronta; espera o serviço REST do Protheus |
| QR da impressão | QRCoder (MIT), SVG e PNG sem System.Drawing | 1.8.0 | Em produção |
| Resiliência | Microsoft.Extensions.Http.Resilience (Polly) | 10.11.0 | Em produção |
| Tempo real | SignalR, com o LISTEN/NOTIFY do Postgres levando os avisos do Worker à API | ASP.NET Core 10, `@microsoft/signalr` 10.0.11 | Em produção |
| Front | React 19 + Vite, React Router, TanStack Query, Zustand, React Hook Form + Zod, Tailwind 4, shadcn/ui (Radix), Recharts, Sonner | React 19.3, Vite 8.3, Router 8.5, Tailwind 4.3, Zod 4.6, TypeScript 6.0, Recharts 3.10.1 | Em produção |
| Leitura de QR e Pix no front | `BarcodeDetector` nativo; sem ele, o polyfill `barcode-detector` sobre o ZXing C++ em WebAssembly (`zxing-wasm`), servido pelo app; `qrcode` para o QR do Pix | barcode-detector 3.2.2, zxing-wasm 3.1.3, qrcode 1.5.4 | Em produção |
| Mapa | Google Maps JavaScript pelo `@googlemaps/js-api-loader`, marcador avançado | loader 2.1.3, tipos 3.66.5 | Em produção |
| Documentação da API | OpenAPI do ASP.NET Core em `/openapi/v1.json` e Swagger UI em `/swagger` | OpenApi 10.0.13, Swashbuckle.AspNetCore.SwaggerUI 10.3.0 | Só em Development |
| Infra | Linux, Docker Compose, Nginx, Let's Encrypt (certbot) | nginx 1.28, certbot 5.8.0, postgres 17 | Em produção no VPS |
| CI e CD | GitHub Actions, runner próprio no deploy, SonarCloud, Sentry | sonarscanner 11.3.0, Sentry 6.12.0 e 11.5.0 | Em uso |

A versão do .NET e a de cada pacote moram só em `Directory.Build.props` e
`Directory.Packages.props`; as do front, em `web/package.json`.

## 2. Arquitetura

```
Front (React) ──┐
Página do QR ───┼── Nginx ── Api ──┬── PostgreSQL 17
                │                  ├── Pagar.me, D4Sign, Resend, Google Places, BrasilAPI
Webhooks ───────┘   Worker ────────┴── Gotenberg (PDF, rede interna), Protheus
```

| Projeto | Responsabilidade | Não pode |
| --- | --- | --- |
| `AvaliaSales.Domain` | Entidades e regras: revenda (`Revendedor`, `Endereco`, marca), catálogo, pedidos e pacotes, lote e placa, venda e cliente, cobrança e parcelamento, razão, financeiro e repasse, contrato e distrato, chamados, ordens de serviço, notificações, auditoria; e as regras puras `CodigoDaPlaca`, `Destino`, `LinkDeAvaliacao`, `Comissao`, `Documentos`, `Telefones`, `HorarioDeAtendimento`, `Parcelamento`, `Reconhecimento` | Depender de outro projeto ou pacote |
| `AvaliaSales.Application` | Casos de uso por área (cadastro e conta, catálogo, pedidos, estoque e placa, vendas e cobrança, comissões e razão, financeiro, contratos e encerramento, chamados, serviços, notificações, auditoria, importação) e as interfaces de fora (`IBancoDeDados`, `IRevendedorAtual`, `IUsuarioAtual`, `IContas`, `IGatewayDePagamento`, `IAssinaturaEletronica`, `IEnvioDeEmail`, `ILugares`, `IDocumentos`, `IConsultaDeEndereco`, `ITransferenciaDeClientes`, `IConferenciaDeAssinatura`, `IErp`) | Conhecer HTTP, Npgsql, Identity ou escrever SQL |
| `AvaliaSales.Infrastructure` | `BancoDeDados` (com as travas de linha em SQL), migrations, Identity e sessões, clientes do Pagar.me, D4Sign, Resend, Places, BrasilAPI, Gotenberg e Protheus, simuladores de Development, modelos do contrato e do distrato, impressão do lote, caixa de entrada, processador, retomadas, envio de e-mails | Ter regra de negócio |
| `AvaliaSales.Api` | Rotas por área (acesso, conta, operadora, revenda, equipe, catálogo, chamados, financeiro, leitura do QR, webhooks), autenticação, políticas por papel, middlewares de conta encerrada e de auditoria da operadora, modos de linha de comando | Ter regra de negócio ou SQL |
| `AvaliaSales.Worker` | Volta a cada 10 segundos (`Rotinas`): eventos dos webhooks, retomadas, e-mails e ERP, cada rotina no próprio escopo e no próprio `try` (`Rotinas.Rodar`), e o check-in no Sentry | Ter regra de negócio |
| `web` | Front único em React: telas, sessão, chamadas à API | Calcular regra de negócio que a API já decide |

Regra recusada sai do Domain como `RegraDeNegocioException`, e a API responde
422. Falha de sistema externo vira `IntegracaoException`, sem chave nem URL na
mensagem. Pedido abortado por quem chamou responde 499 e não vira erro.

### Avisos ao vivo

- Hub `HubDeAvisos` em `/hubs/avisos`, com o token na query só nessa rota
  (WebSocket não leva cabeçalho). Cada conexão entra no grupo da própria
  revenda, lida do token; a operadora entra no grupo dela.
- O aviso leva só o tipo (`venda`, `pagamento`, `pedido`, `estoque`,
  `comissao`, `erp`, `chat`). A tela refaz a consulta pela API, que aplica o
  isolamento: nenhum dado de cliente trafega pelo hub.
- Ponte entre processos - `IBancoDeDados.AvisarAoVivo` guarda o aviso, e o
  `SaveChanges` manda `pg_notify` na mesma conexão. Dentro de transação, o
  Postgres só entrega no commit; transação desfeita não avisa. A API escuta o
  canal (`LISTEN`, `OuvinteDeAvisos`) e repassa ao grupo. Assim o pagamento
  confirmado pelo Worker chega à tela aberta na API, sem Redis (ADR 0013).
- A conexão de escuta que cai tenta de novo com espera crescente até 60 s, e o
  front reconecta sozinho. Perder um aviso só atrasa a tela até a próxima
  consulta.

## 3. Isolamento entre revendas

Um banco só, com `revendedor_id` em toda tabela de negócio e duas travas
independentes (ADR 0001):

1. Filtro global do EF Core - toda entidade que implementa `IDoRevendedor`
   ganha o filtro sozinha, montado por expressão em `BancoDeDados`. A revenda
   de um registro novo sai sempre da requisição e não muda depois.
2. RLS no PostgreSQL - `RevendedorNaConexao` grava `app.revendedor_id` na
   sessão a cada conexão aberta, e a política de cada tabela lê esse valor.
   `FORCE ROW LEVEL SECURITY` vale também para o dono das tabelas. A API
   conecta com o papel `avalia_app`, sem superusuário e sem `BYPASSRLS`; quem
   migra é o dono do banco.

- De onde vem a revenda - na API, só do token (`AcessoDaRequisicao`); no
  Worker, da própria rotina (`RevendedorDaExecucao`), uma revenda por escopo.
  A leitura do QR chega sem login: descobre a revenda da placa em
  `placas_emitidas`, abre um escopo novo e assume essa revenda antes da
  primeira consulta (`AcessoDaRequisicao.Assumir`, que recusa se já houver
  revenda definida).
- Operadora - não pertence a revenda (`usuarios.revendedor_id` nulo, com check
  no banco). Vê e altera só as tabelas marcadas com `IAtendidoPelaOperadora`
  (`pedidos_placas`, `chamados`, `mensagens`, `notificacoes` e
  `ordens_de_servico`): o filtro do EF ganha `OR operadora`, e a política de
  RLS lê `app.operadora = 'sim'`, gravado pela mesma conexão. As demais
  tabelas continuam fechadas a ela (ADR 0008).
- Gravação da operadora - o carimbo do EF aceita a revenda informada só
  quando quem grava é a operadora e a tabela é atendida por ela. Para todos os
  outros, a revenda continua saindo do token, mesmo que o registro venha com
  outra. A política de RLS barra o resto (ADR 0015).
- Tabelas sem RLS de propósito - `usuarios`, `sessoes`, `links_de_senha` e
  `aparelhos_conhecidos` (o login lê antes de saber a revenda);
  `revendedores` (a lista que o Worker percorre); `vinculos_externos` (o
  webhook chega sem revenda, e é ela que diz de quem é); `eventos_externos` e
  `emails` (não têm revenda); e as tabelas da plataforma `pacotes`, `cupons`,
  `lotes`, `placas_emitidas`, `codigos_reservados`, `codigos_de_link`,
  `produtos_da_plataforma`, `envios_ao_erp` e `auditoria_da_operadora`
  (estoque da Avalia antes do despacho, o código de cada placa e de cada link,
  o catálogo e as filas da operadora). O `revendedor_id` de `lotes`,
  `placas_emitidas` e `codigos_de_link` diz de quem é o código.
- Transferência no encerramento - mudar a revenda de uma linha só acontece
  dentro da função `transferir_clientes_da_revenda`, com políticas próprias
  que valem só com `app.transferencia` (ADR 0019).
- Testes - cada trava é testada sozinha, e
  `Toda_tabela_com_revendedor_tem_rls_ligado_e_forcado` falha se uma tabela
  com `revendedor_id` nascer sem RLS. Desligar uma trava de propósito faz o
  teste dela falhar.

## 4. Front

- Contrato - `web/openapi.json` é o OpenAPI da API, versionado e conferido
  pelo `ContratoDaApiTest`. `npm run tipos` gera `src/api/esquema.d.ts` com o
  openapi-typescript, e `src/api/tipos.ts` só apelida os tipos gerados. O CI
  falha se o contrato ou os tipos ficarem para trás.
- Rotas por papel - entrada, cadastro, senha e confirmação abertas;
  `/operadora/*` só para a operadora; `/painel/*` só para o papel `Dono`;
  `/vender`, `/carteira`, `/catalogo`, `/links`, `/placa/:codigo` e
  `/vendedor/*` para `Dono` e vendedor; `/conta/*` para todos. Papel errado
  vai para a área do próprio papel.
- Carga por tela - cada tela carrega o próprio código quando abre (`lazy` do
  React Router), e o vendedor não baixa a área da operadora. Aba aberta antes
  de uma publicação recarrega uma vez ao abrir uma tela cujo arquivo mudou
  (`lib/recarga.ts`, com trava de 1 minuto contra laço), e a raiz das rotas
  tem `ErroDaTela`.
- Sessão - o token de acesso fica só em memória (Zustand), nunca em
  `localStorage` ou `sessionStorage`. Ao abrir, o app chama `/auth/renovar`
  com o cookie HttpOnly; as rotas esperam essa tentativa antes de mandar para
  a entrada, e recarregar a página mantém o login. A chamada renova um minuto
  antes de vencer e, num 401, renova uma vez e repete. Como o refresh é
  rotativo, renovações simultâneas esperam a mesma, e `navigator.locks`
  serializa a renovação entre abas. O front só encerra a sessão no 401; no 429
  ou com o servidor fora, espera e tenta de novo.
- Chamadas - todas por `chamarApi`, com o prefixo `/api`. Em desenvolvimento
  o Vite repassa `/api` à API local sem o prefixo; em produção o Nginx faz o
  mesmo. A mensagem de erro da tela vem do `detail` do ProblemDetails.
- Dados - TanStack Query para leitura e gravação, com a lista refeita depois
  de gravar e a confirmação na tela pelo Sonner (`meta.sucesso`, lido no
  `MutationCache` do `main.tsx`). Formulários com React Hook Form e Zod; a API
  valida de novo.
- Dinheiro - digitado em reais e convertido para centavos inteiros por texto,
  sem ponto flutuante (`paraCentavos`).
- Tema - `web/src/estilos/tokens.css`, em OKLCH: escala neutra fria, a
  primária padrão da plataforma (azul de operação) e cores de estado em par
  fundo e texto, com Geist como face única. Contraste conferido - texto 16:1,
  secundário 5,6:1, branco sobre a primária 6,6:1, borda de campo 3,4:1.
  Variantes `dark:` só com a classe `.dark`, que ainda não existe. Regras e
  componentes em [DESIGN.md](../DESIGN.md); o lint recusa degradê, vidro,
  roxo, valor solto e cor fora dos tokens.
- Toque - campos e botões com 44 px abaixo de 768 px e 36 px acima.
- White label - depois do login, o app lê `GET /marca` e grava só as
  variáveis de marca em `:root` (`--primary`, `--primary-foreground`,
  `--primary-texto`, `--ring`, `--destaque`, `--chart-1`, `--chart-2`), em
  `marca/marca.ts`. O logo vem com o token e vira URL local, com a versão da
  marca na chave. A conta de contraste é a mesma no front (`marca/contraste.ts`)
  e no Domain (`Contraste`).
- Balcão - a Nova venda monta a venda por itens: a placa abre a leitura do QR
  pela câmera traseira (ou o código digitado) e o destino; o serviço entra com
  a quantidade. Tudo sai numa cobrança só (`POST /vendas/itens`). A leitura da
  URL de uma placa em estoque abre a Nova venda com o código. A venda
  registrada mostra cada placa com o NFC e o link curto, e o Pix do total; a
  tela consulta `GET /vendas/{id}/pagamento` a cada 5 s até pagar, falhar ou
  vencer. O NFC não é lido: a tela grava o endereço da placa na tag (Web NFC,
  só Chrome do Android), no fim da venda e na ficha.
- Ponta a ponta - Playwright (`web/e2e`, `npm run e2e`) contra o front
  compilado (vite preview) e a API local com a demonstração: entrada, venda no
  balcão até o link curto, cartão, convite do vendedor, jornada entre
  operadora, revenda e vendedora, segundo fator, encerramento de conta e todas
  as telas do revendedor e da operadora, a 390 px e a 1440 px. Cada tela passa
  no axe (WCAG 2.2 AA; violação séria ou crítica falha) e sem rolagem lateral.
  Roda com movimento reduzido, para o axe não medir botão no meio da
  transição. No CI, o job `e2e` sobe Postgres, migra, semeia e liga a API
  pelo `web/e2e/api.sh`.
- Testes - Vitest e Testing Library: dinheiro, sessão, renovação (uma só para
  chamadas simultâneas), rota protegida por papel, código da placa,
  percentual, máscaras, endereço, recarga depois de publicação, contraste da
  marca e a tela de entrada. Cada história do Storybook roda no Chromium com o
  axe em modo erro.

## 5. Modelo de dados

Dinheiro em centavos (`bigint`, colunas `_cents`), taxa em pontos base
(`_bps`). Datas em UTC (`timestamptz`). Situações gravadas como texto.
Migration só aditiva: a versão anterior continua lendo o banco depois da
migração. Tabela nova com `revendedor_id` chama `IsolarPorRevendedor` no fim
do `Up`.

| Tabela | Guarda | Regra |
| --- | --- | --- |
| `revendedores` | A revenda - nome, CNPJ, celular (só dígitos, com DDD), endereço (`endereco_logradouro`, `_numero`, `_complemento`, `_bairro`, `_cidade`, `_uf`, `_cep`), recebedor no Pagar.me (`recebedor_pagarme`), taxa da Avalia (`taxa_avalia_bps`, padrão 0), comissão padrão (`comissao_padrao_bps`, 2000 = 20%), renovação (`renovacao_bps`, 6000 = 60%), marca (`nome_exibido`, `cor_primaria`, `cor_destaque`, `cabecalho_na_cor`, `marca_alterada_em`), `contrato_assinado_em`, encerramento (`encerramento_pedido_em`, `encerrada_em`, `reabertura_pedida_em`, `reaberta_em`) e suspensão (`suspensa_em`, `motivo_da_suspensao`) | Sem RLS; CNPJ único |
| `usuarios`, `papeis` e demais do Identity | Login, `papel` (Operadora, Dono, Vendedor), `ativo`, revenda e `segundo_fator_por_email` | Sem RLS; e-mail único na plataforma; check: operadora sem revenda, os outros com |
| `sessoes` | Refresh token pelo hash SHA-256, família do login, validade de 14 dias | Sem RLS; hash único |
| `links_de_senha` | Convite, redefinição e confirmação de e-mail: hash do token, finalidade, validade, `hash_do_codigo` e `erros_do_codigo` | Sem RLS |
| `aparelhos_conhecidos` | Hash do aparelho por usuário, primeiro e último acesso | Sem RLS |
| `vendedores` | Equipe, com o mesmo id do login: telefone, CPF, chave Pix, comissão própria, meta do mês (`meta_mensal_cents`), ativo | RLS |
| `produtos_da_plataforma` | Catálogo da Avalia - chave (única), nome, descrição, tipo (Placa, PlacaComServico, Servico), preço sugerido, desconto da revenda (`desconto_da_revenda_bps`, padrão 2000), pedido mínimo (`quantidade_minima`), situação (Disponivel, Esgotado, ForaDeLinha), ordem, foto padrão do front (`/produtos/*.jpg`) e foto enviada (bytea até 512 KB, tipo e versão) | Sem RLS |
| `produtos` | Preço da revenda para um produto do catálogo (`produto_da_plataforma_id`, único por revenda) e comissão do produto (`comissao_bps`). O produto sem catálogo é o de antes dele, só para as vendas antigas | RLS |
| `pacotes` | Quantidade e preço oferecidos pela operadora | Sem RLS; quantidade única |
| `cupons` | Código, desconto (pontos base ou centavos), validade, limite e usos | Sem RLS; código único; travado na hora do uso |
| `pedidos_placas` | Pacote copiado (quantidade, `preco_do_pacote_cents`), cupom, desconto, valor, situação, pagamento, `producao_em`, despacho (envio ou `retirada`), lote e ativação | RLS com a operadora |
| `lotes` | Número, quantidade, título, categoria, pedido e revenda do despacho, ativação, `arquivado_em` e `baixado_em` (primeiro download) | Sem RLS; número e pedido únicos |
| `placas_emitidas` | Código (chave), sequência da plataforma, lote, revenda depois da ativação | Sem RLS; só identificadores (ADR 0007) |
| `codigos_reservados` | Código que existe fora da plataforma e a origem | Sem RLS; o sorteio pula |
| `placas` | Placa no estoque da revenda: custo congelado, situação, consignação, destino, venda, vencimento, leituras, baixa (`motivo_da_baixa`, `baixada_em`) e a transferência no encerramento (`transferida_em`, `cliente_da_transferencia_id`, `valor_da_venda_cents`) | RLS; código único; políticas `transferencia_no_encerramento` (UPDATE) e `_leitura` (SELECT), também em `clientes` e `links` |
| `leituras` | Leituras por placa e dia | RLS; chave (placa, dia), soma por `on conflict` |
| `clientes` | Negócio que comprou: nome, responsável, documento, telefone, e-mail, lugar no Google (só o id), `anonimizado_em` | RLS; documento único por revenda |
| `vendas` | Placa (nula na venda de serviço), cliente, produto, vendedor, quantidade, grupo (`grupo_id`), valor, tabela, custo, parte da Avalia (`parte_da_avalia_cents`), comissão, cortesia, repasse que pagou a comissão, cancelamento (`motivo_do_cancelamento`, `observacao_do_cancelamento`, `cancelada_em`), teste gratuito (`teste_ate`, `teste_avisado_em`, `teste_expirado_em`, `teste_convertido_em`) e `do_sistema_anterior` | RLS; uma venda registrada por placa (o índice único de `placa_id` só vale para venda registrada, e a placa da venda cancelada vende de novo); índice parcial nos testes em curso |
| `cobrancas` | Cobrança: origem (venda, pedido de placas, renovação) e id da origem, meio, valor, taxa da Avalia com a parte do serviço (`taxa_avalia_cents`), `estornado_cents`, situação, pedido e link no Pagar.me, URL do checkout, Pix (copia e cola, QR, vencimento), `conferida_em` | RLS; `pedido_no_gateway` único; taxa copiada na criação |
| `renovacoes` | Placa, venda, valor, situação, de, até, paga em | RLS; uma aberta por placa |
| `lancamentos` | Razão da revenda: tipo (`Receita`, `TaxaAvalia`, `ComissaoDevida`, `ComissaoPaga`, `CustoDaPlaca`, `Perda`, `Despesa`, `RetiradaDeSocio`, `AporteDeSocio`, `SaldoInicialDoCaixa`), origem, venda, vendedor, valor com sinal, competência, `categoria` (da despesa ou natureza da retirada), `placa_id` e a linha estornada (`estorna_id`) | RLS; só inserção, com `REVOKE UPDATE, DELETE` do papel da aplicação (ADR 0011); único (origem, tipo, venda, vendedor), com nulos iguais, na linha original e único `estorna_id` no estorno; gatilho `lancamento_em_mes_aberto` passa ao mês corrente a linha de mês fechado (ADR 0020). Despesa, retirada e perda usam origem nova por versão, para o índice aceitar a correção |
| `repasses` | Comissão paga ao vendedor: valor, vendas, data de corte, comprovante, `desfeito_em` | RLS; vendedor travado na gravação |
| `despesas` | Data, descrição, categoria, valor e a linha do razão em vigor (`lancamento_id`) | RLS |
| `socios` | Sócio do contrato social: nome e CPF opcional | RLS; não se exclui com retirada |
| `retiradas` | Sócio, data, natureza, valor, observação e a linha do razão em vigor | RLS |
| `fechamentos` | Mês fechado: competência, fechado em e por, reaberto em e por, motivo | RLS; um em vigor por mês (índice único com `reaberto_em is null`) |
| `contratos` | Tipo (`ContratoDoRevendedor`, `TermoDoCliente`, `Distrato`), PDF, hash SHA-256, signatário, documento na D4Sign, etapa do envio, tentativas, situação, `conferido_em`, `reenviado_em` | RLS; `documento_na_assinatura` único |
| `vinculos_externos` | Código no Pagar.me ou documento na D4Sign, e a revenda dona | Sem RLS; chave (origem, id_externo); só identificadores |
| `eventos_externos` | Webhook cru, chave do vínculo, tentativas, último erro, processado em | Sem RLS; único (origem, id_externo) |
| `emails` | Fila de e-mails com chave única, nome do remetente, resposta, cor da marca, tentativas, próxima tentativa e id no Resend | Sem RLS |
| `envios_ao_erp` | Pedido pago a caminho do Protheus: cliente, CNPJ, quantidade, valor, situação, número no ERP, tentativas, erro | Sem RLS (fila da operadora); um por pedido |
| `links` | Link curto da revenda: código, destino, título, cliente, ativo, cliques | RLS |
| `codigos_de_link` | Código de cada link e a revenda | Sem RLS; o código nunca volta ao sorteio |
| `chamados` | Número (identidade do banco), revenda, aberto por, assunto, situação (AguardandoAvalia, AguardandoRevenda, Resolvido), aberto em, `prazo_em`, primeira resposta, resolvido em, última mensagem em | RLS com a operadora |
| `mensagens` | Mensagem de chamado: chamado, autor, se é da operadora, texto (até 2.000), enviada em, lida em | RLS com a operadora |
| `ordens_de_servico` | Número (identidade), revenda, venda (única), produto do catálogo, produto e cliente gravados, quantidade, situação (AFazer, EmAndamento, Entregue, Cancelada), observação, criada, iniciada e entregue em | RLS com a operadora |
| `notificacoes` | Revenda, para a operadora ou para a revenda, destinatário (`usuario_id`, para o vendedor), venda (`venda_id`), título, texto, link, criada e lida em | RLS com a operadora |
| `logos` | Logo da revenda (PNG ou WebP, até 512 KB) | RLS; tabela própria, para a leitura da revenda em toda requisição não carregar a imagem |
| `auditoria` | Usuário, ação, alvo, referência, antes e depois, ocorrido em | RLS; só inserção (`REVOKE UPDATE, DELETE`, com teste) |
| `auditoria_da_operadora` | Usuário, ação (método e rota com os parâmetros), referência, corpo enviado em JSON (até 2.000 caracteres), ocorrido em | Sem RLS; só as rotas `/operadora/auditoria` leem |

Se a venda está paga, quem diz é a cobrança mais recente com origem nela, e
não uma coluna da venda: a cobrança gravada antes de o Pagar.me cair já aponta
para a venda.

## 6. Integrações

Cada integração mora atrás de uma interface na Application, com a
implementação na Infrastructure. Retentativa automática só em método seguro:
`AddStandardResilienceHandler(o => o.Retry.DisableForUnsafeHttpMethods())`,
porque o padrão do handler repete também POST, e um POST de cobrança repetido
cobra duas vezes. Um teste falha se essa opção sair.

### Pagar.me

Em produção desde 23/10/2026, na conta da Avalia (marketplace), com o
recebedor da Avalia em `re_` e o webhook ligado. Endereços e campos
conferidos na referência oficial em 21/10/2026.

- Endereço e chave - `https://api.pagar.me/core/v5/` em teste e produção; a
  chave secreta define o ambiente (`sk_test_` ou `sk_`). Basic auth com a
  chave como usuário e senha vazia.
- Criar pedido - `POST /orders` com `code` igual ao id da cobrança (32
  caracteres), um item, o cliente e o pagamento em Pix (`expires_in` em
  segundos, 30 minutos por padrão) ou cartão (`card_token`).
- Cartão no balcão - o navegador manda número, validade, nome e CVV direto a
  `POST https://api.pagar.me/core/v5/tokens?appId={pk}` com a chave pública
  (`Pagarme__ChavePublica`, `pk_test_` ou `pk_`) e recebe um token de uso
  único. A API recebe só o token e cria o pedido com
  `credit_card.card_token`, parcelas e o mesmo split. `GET /pagamento/cartao`
  diz ao front se o cartão está disponível e o custo de cada parcela
  (`custoDasParcelasBps`). Sem chave pública, a venda é só no Pix.
- Parcelamento - `Domain/Pagamentos/Parcelamento` guarda as condições do
  contrato com o Pagar.me (Pix 109 bps; cartão pelo custo efetivo de 1x a
  12x). De 2x em diante a cobrança sai por `valor / (1 - custo)`, arredondado
  para cima, e o front mostra o mesmo cálculo (`lib/cartao.ts`). No Pix e à
  vista, a parte da Avalia sai do split já sem a fatia dela no custo
  (`Parcelamento.ParteLiquida`). Condição nova do Pagar.me muda a tabela no
  código.
- Split - regras `flat` em centavos: a revenda e, quando há taxa ou parte do
  serviço, a Avalia (ADR 0003). O tipo `percentage` não aceita 1,99. A revenda
  tem `liable`, `charge_processing_fee` e `charge_remainder_fee`. Regra de
  valor zero não é enviada. Revenda com o recebedor igual ao da Avalia vai
  numa regra só, com o valor inteiro, porque o split não repete recebedor; a
  taxa continua gravada na cobrança e no razão. Os recebedores aceitam `re_`
  e `rp_`.
- Checkout do pedido de placas - `POST /paymentlinks` com `type` order,
  `order_code` igual ao id da cobrança, `max_paid_sessions` 1, validade de 3
  dias (`expires_in` em minutos), Pix, cartão e boleto, sem `split_settings`:
  o pedido de placas é todo da Avalia (ADR 0012). O pedido no Pagar.me só
  nasce quando a revenda paga. A conferência de cobrança pendente procura pelo
  código; link vencido sem pagamento vira cobrança cancelada, e outro link
  pode sair.
- Cancelamento e estorno - `IGatewayDePagamento.CancelarPedido` lê o pedido
  (`GET /orders/{id}`) e chama `DELETE /charges/{id}` em cada cobrança
  pendente ou paga. `EstornarParte` faz o mesmo `DELETE` com `amount`, para o
  estorno parcial da venda dentro de um grupo. A recusa 4xx do Pagar.me (por
  exemplo `412 action_forbidden`, saldo insuficiente) vira
  `RegraDeNegocioException`; 5xx e tempo esgotado seguem como erro.
- Recebedor - `GET /recipients/{id}`, usado só pelo comando
  `--conferir-pagarme` (`docs/OPERACAO.md`), que confere a chave e os
  recebedores sem cobrar.
- Sem cabeçalho de idempotência - a referência não documenta nenhum. A
  cobrança é gravada antes da chamada, e quando a chamada cai o pedido é
  procurado por `GET /orders?code=` antes de qualquer nova tentativa (ADR
  0004).
- Situação - lida do pedido (`paid`, `canceled`, `failed`, `pending`);
  estorno e chargeback lidos da cobrança (`refunded`, `chargedback`).
- Webhook - `POST /webhooks/pagarme/{segredo}`. O Pagar.me não assina o
  webhook: o segredo na URL barra quem não a conhece (resposta 404), e o
  Worker reconsulta o pedido antes de mudar qualquer coisa. A chave do evento
  é o `id` do hook; o vínculo é achado pelo `data.code`. O estorno chega pelo
  evento `charge.refunded` ou pela reconsulta diária da cobrança paga.

### D4Sign

Conferido na referência oficial em 21/10/2026. Em produção no ambiente de
testes, sem validade jurídica.

- Endereço e chave - `https://sandbox.d4sign.com.br/api/v1/` por padrão; a
  produção (`secure.d4sign.com.br`) só por configuração (`D4Sign__UrlBase`).
  `tokenAPI` e `cryptKey` vão na URL, por isso a URL nunca entra em erro nem em
  log (`System.Net.Http.HttpClient` em Warning).
- Envio em quatro passos - `uploadbinary` (PDF em base64 no cofre),
  `webhooks`, `createlist` (signatário, `act` 1 = assinar) e `sendtosigner`.
  Cada passo é gravado ao terminar, e a retomada continua do último, sem
  repetir o upload. O `sendtosigner` é idempotente: o 400 "already in the
  signature phase" conta como enviado.
- Reenvio e cópia - `resend` com a `key_signer` de `documents/{uuid}/list`, e
  `download` (URL temporária, aceita só de `*.d4sign.com.br`).
- Limite - a conta padrão aceita 10 requisições por hora, e um envio gasta
  quatro. O Worker retoma no máximo 2 contratos por volta. Para mais volume,
  pedir aumento à D4Sign.
- Situação - `GET /documents/{uuid}`: `statusId` 4 é finalizado, 6 é
  cancelado; os demais são andamento.
- Webhook - `POST /webhooks/d4sign`, em formulário (`uuid`, `type_post`,
  `email`) ou JSON. O cabeçalho `Content-Hmac: sha256=<hex>` é o HMAC-SHA256
  do UUID do documento com a secret key MAC da conta, comparado em tempo
  constante. O HMAC cobre só o UUID, e o Worker reconsulta a situação. A
  chave do evento é `uuid:type_post:email`.
- Reconsulta - o Worker reconsulta, nas Retomadas, quem espera assinatura há
  mais de 30 minutos sem conferência, dentro do mesmo limite de contratos por
  volta. O revendedor pede pelo botão (`POST /revenda/contrato/conferencia` e
  `/revenda/encerramento/conferencia`), uma vez por minuto. Webhook, Worker e
  botão travam a linha do contrato (`select ... for update`) e aplicam os
  efeitos por `EfeitosDaAssinatura`, um caminho só. Falha da D4Sign também
  grava `conferido_em`, para a reconsulta não gastar a cota em sequência.
- Cancelamento - `POST /documents/{uuid}/cancel` com `comment`, quando o
  revendedor desiste do distrato. A resposta documentada não é confiável: se
  a chamada for recusada e o documento já estiver cancelado, conta como
  cancelado. Distrato gerado depois da desistência é cancelado pelo Worker
  antes de ir à D4Sign.

### Documentos em PDF (Gotenberg)

- Contrato versão 2 e distrato saem dos modelos HTML em
  `Infrastructure/Impressao/Modelos`, preenchidos por `DocumentosEmPdf`
  (`{{campo}}` escapado, `{{{campo}}}` HTML montado no código; campo sem valor
  é erro) e convertidos pelo Gotenberg 8.38.1
  (`POST /forms/chromium/convert/html`, conferido em 22/10/2026), com o rodapé
  em `footer.html`, o CSS, o logo e a Charis SIL (OFL). ADR 0018.
- Só o Worker gera, nas Retomadas: contrato de quem não tem, com endereço e
  revendedor confirmado; distrato de quem pediu encerramento e não tem um em
  andamento. Falha do conversor fica no log e volta na próxima rodada.
- Configuração - `Pdf:UrlDoConversor` (obrigatória; `http://gotenberg:3000`
  no Compose). `Plataforma:RepresentanteDaAvalia` e
  `Plataforma:CargoDoRepresentante` saem no bloco de assinatura da Avalia e
  vêm das variáveis do ambiente `producao` do GitHub.
  `Plataforma:CnpjDaRevendaDaAvalia` (padrão 68715987000164) diz para qual
  revenda vão os clientes de quem encerra.

### BrasilAPI

- `GET cnpj/v1/{cnpj}` e `GET cep/v2/{cep}` em `https://brasilapi.com.br/api/`,
  conferidos em 22/10/2026, pelas rotas anônimas `/cadastro/cnpj/{cnpj}` e
  `/cadastro/cep/{cep}` (com o limite da entrada). O CNPJ traz o município em
  maiúsculas sem acento; a cidade, a rua e o bairro do CEP valem mais quando
  vêm. Qualquer falha responde 404, e a tela pede o campo digitado.

### Resend

Conferido na referência oficial em 21/10/2026. Em produção, com o domínio
sales.avaliaone.com.br verificado (SPF, DKIM e DMARC) e as variáveis
`Resend__ChaveApi`, `Resend__Remetente` e `Resend__ResponderPara`.

- `POST https://api.resend.com/emails` com `Authorization: Bearer re_...`,
  `from`, `to`, `subject`, `text`, `html` e `reply_to`. Resposta
  `{ "id": "..." }`.
- `from` é sempre o endereço de `Resend:Remetente`. O e-mail em nome da
  revenda leva o nome dela como exibição
  (`"Padaria via Avalia Sales" <avisos@...>`) e `reply_to` do revendedor; o
  da plataforma responde para `Resend:ResponderPara`. Nome, resposta e cor da
  marca são copiados para a fila na hora (`nome_do_remetente`,
  `responder_para`, `cor_da_marca`).
- O HTML (`ModeloDeEmail`) sai do texto escapado: a linha
  `Rótulo: https://...` vira botão com esse rótulo, na cor da marca, com a
  letra escolhida pelo contraste WCAG; a linha `Rótulo: 123456` mostra o
  código em destaque. E-mail da plataforma leva o logo em PNG
  (`web/public/marca/avaliasales-email.png`, pelo `Plataforma:UrlDoPainel`) e
  a faixa no traço da marca. Quem escreve e-mail novo usa essas duas linhas,
  sem HTML.
- O e-mail entra na tabela `emails` na mesma transação do fato que o motiva,
  com chave única (por exemplo `pedido:{id}:operadora`). O Worker envia com a
  chave no cabeçalho `Idempotency-Key` (até 256 caracteres, vale 24 horas):
  retentativa ou dois Workers não entregam em dobro. A espera dobra a cada
  falha (de 1 minuto até 6 horas), e o envio desiste depois de 20 tentativas,
  uns 3 dias.
- Sem chave configurada, o Worker não envia e o e-mail espera na fila; nada é
  marcado como enviado sem ter saído.
- E-mails - pedido novo e pedido pago para a operadora, lote despachado para o
  revendedor, pagamento a devolver quando chega para pedido já pago ou
  cancelado, confirmação do e-mail, convite do vendedor, troca de senha,
  aparelho novo, chamado novo e resposta de chamado.
- Confirmação do e-mail - `links_de_senha` com finalidade
  `ConfirmacaoDeEmail` (2 dias), `POST /auth/email` e
  `POST /conta/email/confirmacao` para reenviar, recusado antes de 1 minuto do
  envio anterior. O e-mail leva também um código de 6 dígitos (no assunto),
  digitado na tela por `POST /conta/email/confirmacao/codigo`; o banco guarda
  só o hash (`hash_do_codigo`, com o id do link), e o quinto erro encerra o
  link (`erros_do_codigo`). `EmailsDosDonos` só devolve e-mail confirmado,
  então contrato e aviso esperam a confirmação. O perfil do token traz
  `emailConfirmado`.

### Google Places e Maps

Conferido na referência oficial em 21/10/2026.

- Text Search (New) - `POST https://places.googleapis.com/v1/places:searchText`
  (chamado pelo caminho relativo `./places:searchText`) com `X-Goog-Api-Key` e
  `X-Goog-FieldMask: places.id,places.displayName,places.formattedAddress`
  (SKU Text Search Pro), `textQuery`, `languageCode` pt-BR, `regionCode` BR e
  `pageSize` 8.
- A chave do servidor (`Google:ChaveDoServidor`) é restrita à Places API e ao
  IP do VPS. O front chama `GET /lugares?texto=`; guarda-se só o id do lugar,
  que pode ser guardado sem prazo. Sem chave, a rota responde 422 pedindo o
  link colado do Perfil da Empresa.
- Mapa - `GET /mapa` devolve só o id do lugar no Google de cada cliente
  vendido, com placas, placas no ar e leituras de 30 dias. A posição e a
  cidade (`addressComponents`) são lidas no navegador pelo Places
  (`Place.fetchFields`): dado do Places só aparece em mapa do Google e não é
  guardado.
- `GET /mapa/configuracao` entrega a chave do navegador (`Google:ChaveDoMapa`,
  pública por natureza, restrita ao domínio e à Maps JavaScript API) e o Map
  ID (`Google:IdDoMapa`, padrão `DEMO_MAP_ID`) do marcador avançado. O mapa
  abre com `colorScheme: 'DARK'`. Sem chave, a tela mostra a lista com o link
  do Google Maps.

### Protheus

- Sem ambiente público. A plataforma fala um contrato REST próprio
  (ADR 0014): `POST {Protheus:UrlBase}/pedidos-de-venda` com autenticação
  básica e `{ chave, cliente, cnpj, produto, quantidade, valorUnitarioCents,
  valorTotalCents }`, resposta `{ numero }`. Do lado do Protheus, um serviço
  REST em ADVPL grava o pedido de venda; a chave (id do pedido de placas)
  impede pedido de venda repetido.
- Pedido de placas pago (pelo link, por fora ou com cupom de 100%) entra na
  fila `envios_ao_erp` na mesma transação do pagamento. O Worker envia; a
  falha soma tentativa e, depois de 8, o envio para e a operadora reenvia.
- Configuração - `Protheus__UrlBase`, `Protheus__Usuario` e
  `Protheus__Senha`. Sem endereço, o envio espera na fila. Em produção o
  endereço ainda não está configurado.

## 7. Ambientes de teste e simuladores

Conferido nas páginas oficiais em 21/10/2026.

| Serviço | Ambiente de teste | Limites e ressalvas |
| --- | --- | --- |
| Pagar.me | Chaves `sk_test_` e `pk_test_` no mesmo endereço da produção | Pix simulado aprova até R$ 500,00 e falha acima; o simulador de Pix não funciona com split, então o teste de split usa cartão (`4000000000000010` aprova, `4000000000000028` recusa). Split exige conta PSP; a conta da Avalia é marketplace |
| D4Sign | `sandbox.d4sign.com.br`, cadastro próprio | Sem validade jurídica; mesmo limite de 10 requisições por hora até a D4Sign aumentar |
| Resend | Endereços de teste `delivered@resend.dev`, `bounced@resend.dev`, `complained@resend.dev` | Plano gratuito com 3.000 e-mails por mês e 100 por dia; o teste conta na cota |
| Google Maps e Places | Sem ambiente de testes: toda chamada conta na cota | Cota gratuita por SKU por mês (10.000 carregamentos de mapa dinâmico, 10.000 Place Details Essentials); exige conta de faturamento no Google Cloud |
| Protheus | Sem ambiente público | Simulador próprio, só em Development |
| Sentry | Plano Developer gratuito | 1 usuário, 5 mil erros, 30 dias de histórico |
| SonarCloud | Gratuito para repositório público | Privado: gratuito até 50 mil linhas |

Simuladores - `Pagarme:Simulado`, `Protheus:Simulado` e `Pdf:Simulado` só
ligam em Development (`appsettings.Development.json`); a API e o Worker
recusam subir com eles em outro ambiente.

- Pagar.me (ADR 0009) - `GatewaySimulado` sem rede e sem estado: o código do
  pedido é o id da cobrança (UUID v7), que carrega a hora da criação. A API e
  o Worker, processos separados, chegam à mesma resposta: pendente nos
  primeiros 20 segundos e paga depois. O pagamento simulado chega pela
  conferência de cobrança pendente, o mesmo caminho que cobre webhook perdido
  em produção. O link de pagamento simulado abre
  `/simulador/pagarme/{codigo}`, uma página da API que diz que nada é cobrado.
  No cartão, o front gera um token simulado e o cartão aprova na hora (final
  0002 recusa).
- Protheus - `ErpSimulado` devolve um número de pedido tirado da chave.
- PDF - `ConversorSimulado` gera um PDF mínimo, sem o contêiner do Gotenberg.

## 8. Webhooks, Worker e efeitos

### Webhooks

1. A rota grava o evento cru em `eventos_externos` com
   `insert ... on conflict do nothing` e responde 200. Reenvio do mesmo evento
   não grava nada.
2. O Worker lê os pendentes, acha a revenda pelo vínculo e reconsulta a fonte
   fora de transação.
3. Abre um escopo já isolado na revenda, trava o evento com
   `for update skip locked` e aplica o efeito e a marca de processado na mesma
   transação. Dois Workers no mesmo evento: um aplica, o outro não acha linha.
4. A falha soma tentativa e guarda o erro; depois de 10 tentativas o evento
   fica parado. Evento de pedido ou documento que não é da plataforma é
   encerrado com nota.

### Retomadas

Revenda por revenda, a cada volta:

- Cobrança pendente sem pedido há mais de 2 minutos é procurada pelo código, e
  vira falha depois de 15 minutos sem pedido.
- Conferência de cobrança pendente - rede de segurança do webhook perdido. O
  Worker reconsulta pelo código a cobrança pendente com pedido, criada há
  menos de 2 dias, a cada `Pagarme:ConferenciaEmSegundos` (300 em produção,
  15 no simulador), no máximo 20 por revenda a cada volta, e grava
  `conferida_em`.
- Cobrança paga - a cobrança paga com pedido, de até 120 dias, volta à
  conferência uma vez por dia, 5 por revenda a cada volta. Estorno ou
  chargeback feito no painel do Pagar.me sem webhook estorna aqui, com os
  lançamentos de volta no razão (`ChargebackTest`).
- Contrato com envio incompleto retoma do último passo, até 5 tentativas;
  contrato e distrato a gerar; reconsulta de assinatura.
- Teste gratuito (`TestesGratuitos.Processar`) - avisa dois dias antes do fim
  e tira do ar a placa do teste vencido sem pagamento.
- Fim do prazo de exportação - passados 30 dias do encerramento, desliga o
  login do revendedor.

### Efeitos do pagamento

- `EfeitosDaCobranca` roda quando a situação da cobrança muda, antes do mesmo
  `SaveChanges`: pedido de placas pago (com a entrada na fila do ERP), razão
  da venda paga (receita, taxa, comissão devida), venda paga de serviço
  virando ordem de serviço, renovação estendendo a placa, estorno com as
  linhas contrárias.
- Estorno (`EfeitosDaCobranca.Estornar`) - cancela as ordens de serviço
  abertas da venda e avisa a operadora da ordem entregue.
- A cobrança nasce dentro de uma transação com trava de aplicação pela origem
  (`IBancoDeDados.TravarOrigemDaCobranca`) e recusa a segunda cobrança viva
  da mesma venda.
- Recebido por fora (`MeioDePagamento.Dinheiro` e `OutroPix`) é uma cobrança
  que nasce paga, sem pedido no Pagar.me, e passa pelos mesmos efeitos no
  mesmo `SaveChanges`; a linha da taxa diz "a acertar". Desfazer leva a
  cobrança a estornada, e os efeitos lançam o contrário.
- Cancelamento (`Vendas.Cancelar`) - a venda se cancela antes da cobrança,
  depois cada cobrança viva é cancelada ou estornada (`Cobrancas.Cancelar`,
  `Cobrancas.EstornarVenda` no grupo), e as notificações da venda são
  encerradas (`notificacoes.venda_id`).
- Troca de placa - trava a placa nova (`FOR UPDATE`) e encerra a antiga com o
  motivo (`Placa.ReceberVendaDe`).
- Custo da placa vendida e apropriação da receita são lidos do razão na DRE
  (`Dre`, `Domain/Financeiro/Reconhecimento`), e não lançados.

### Encerramento da conta

- Rotas do revendedor - `GET/POST/DELETE /revenda/encerramento`,
  `POST /revenda/encerramento/reenvio`, `GET /revenda/encerramento/pdf` e
  `GET /revenda/exportacao` (JSON com clientes, vendas, placas e razão).
- Distrato assinado (webhook e reconsulta, como o contrato) aplica
  `EfeitosDoEncerramento` na transação do evento: `encerrada_em`, baixa do
  estoque, vendedores e logins de vendedor desligados e
  `transferir_clientes_da_revenda` para a revenda da Avalia. Distrato
  cancelado na D4Sign desfaz o pedido.
- O middleware `ContaEncerrada` recusa com 409 toda escrita da revenda
  encerrada e a busca no Google (`/lugares`); passam `/auth`, o aviso ao vivo,
  `/notificacoes`, `/chamados`, `/conta/senha`, `/conta/segundo-fator` e
  `/revenda/reabertura`. O front esconde as telas de venda
  (`app/contaEncerrada.ts`) e mostra `AvisoDeContaEncerrada`.
- Reabertura - `POST /revenda/reabertura` (motivo opcional) grava
  `reabertura_pedida_em` e abre o chamado "Reabertura da conta";
  `POST` e `DELETE /operadora/revendedores/{id}/reabertura` aprovam ou
  recusam. Aprovar limpa o encerramento, religa os logins do papel `Dono`,
  zera `contrato_assinado_em` e grava `reaberta_em`: o Worker gera o contrato
  novo, porque só conta contrato criado depois de `reaberta_em`.

### Chamados

- O revendedor usa `/chamados` (lista e abre) e a operadora
  `/operadora/chamados` (a fila); os dois leem em `{id}`, respondem em
  `{id}/mensagens` e resolvem em `{id}/resolver`.
- O prazo sai de `HorarioDeAtendimento.PrazoDaResposta` no Domain: 24 horas
  só em dia útil (segunda a sexta, sem feriado nacional fixo nem
  Sexta-feira Santa, pela Páscoa de Meeus), começando na abertura das 8h
  quando chega fora do expediente, no fuso America/Sao_Paulo. O chamado guarda
  o prazo em `prazo_em`; atrasado é `agora > prazo_em`, calculado na leitura.
- Mensagem nova manda o aviso `chat` ao outro lado; abrir a conversa marca as
  do outro lado como lidas.

## 9. Segurança e LGPD

| Ponto | Situação |
| --- | --- |
| Token de 15 minutos, emissor, público e algoritmo validados, chave de 32+ caracteres do ambiente | Em produção |
| Login com resposta igual para e-mail inexistente e senha errada, bloqueio após 5 erros, 20 tentativas por minuto por IP na entrada, nos links de senha e na confirmação. Renovar e sair têm limite próprio de 300 por minuto (`LimiteDeRenovacao`), porque a renovação roda a cada página e uma loja sai por um IP só | Em produção |
| Senha por tamanho (10+), sem regra de tipo de caractere | Em produção |
| Webhooks com limite de 120 por minuto por IP e corpo de até 1 MB | Em produção |
| Segredos só no ambiente; configuração das integrações validada na partida | Em produção |
| Papéis operadora, `Dono` e vendedor no token, com política por área da API; operadora sem revenda | Em produção |
| Refresh token rotativo em cookie HttpOnly e SameSite Strict, 14 dias, só o hash no banco; token já trocado apresentado de novo encerra o login inteiro (RFC 9700, ADR 0006) | Em produção |
| Login desativado não entra e perde as sessões abertas | Em produção |
| Convite do vendedor e "esqueci a senha" por link de uso único (`links_de_senha`, só o hash; convite 24 horas, redefinição 1 hora; link novo derruba o anterior). O token vai depois do `#`, fora de log e de Referer. Pedido de link responde igual com e sem conta. Senha trocada destrava o login e encerra as sessões abertas. Troca logada (`POST /conta/senha`) confere a senha atual com o bloqueio da entrada, encerra todas as sessões, abre a deste aparelho e avisa por e-mail | Em produção |
| Leitura do QR com limite de 300 por minuto por IP, sem cookie; robôs não contam | Em produção |
| CSP no HTML do front (Nginx): só o próprio domínio, Pagar.me (token do cartão), Google Maps e Places, Sentry e `wasm-unsafe-eval` para o leitor de QR; `frame-ancestors 'none'`. HSTS, nosniff, Referrer-Policy e Permissions-Policy só com câmera e NFC | Em produção, conferida com `curl -sI` em 23/10/2026 |
| Log de acesso do Nginx sem query string (formato `sem_query`), porque busca e filtro levam nome e telefone | Em produção |
| Segredos - gitleaks no CI (job `segredos`), sem achado no histórico; falso positivo conferido no `.gitleaksignore` | Em uso |
| Auditoria - tabela `auditoria` com RLS, só de inserção, gravada na transação da ação; `GET /auditoria` só do papel `Dono` (20 por página, filtro por `acao` e `busca` no alvo, código, antes e depois, ação e vendedor), `GET /auditoria/exportar` em JSON com os mesmos filtros e teto de 10.000, e o histórico na ficha | Em produção |
| Auditoria da operadora - o middleware `AuditoriaDaOperadoraNaRota` no grupo `/operadora` grava cada escrita com resposta abaixo de 400 (corpo JSON com buffer; arquivo não) | Em produção |
| Aviso por e-mail quando a conta entra de um aparelho novo (`aparelhos_conhecidos`, cookie `avalia_aparelho` de 2 anos, só o hash; o primeiro entra calado) | Em produção |
| Revendedor só opera com e-mail confirmado (claim `email_confirmado` nas políticas); sem ele, só `/conta` | Em produção |
| Segundo fator (app TOTP ou código por e-mail) opcional para revendedor e operadora: entrada em dois passos com desafio de 5 minutos, 10 códigos de recuperação, erro de código conta para o bloqueio, link de senha não pula o fator (ADR 0017) | Em produção |
| LGPD - exportação dos dados do cliente (`GET /clientes/{id}/dados`) e anonimização (`POST /clientes/{id}/anonimizacao`, recusada com cobrança em aberto), só pelo papel `Dono` e dentro da revenda; os valores ficam no razão. Logs e Sentry sem corpo, cookie nem IP | Em produção; depois do distrato, exportação por 30 dias e guarda do que a lei exige (PDD, seção 8) |

Revisão pela skill `seguranca` (OWASP ASVS 5.0).

## 10. Infraestrutura

- Dockerfile - uma compilação e duas imagens: `api` (que também roda
  `--migrar` como dono do banco) e `worker`. As duas rodam sem root.
- Docker Compose de desenvolvimento - `postgres` (17, papel `avalia_app`
  criado na primeira subida), `migrar` (termina antes das outras), `api` e
  `worker`. A configuração das integrações é obrigatória: sem ela, API e
  Worker não sobem. Segredos no `.env`, modelo em `.env.example`.
- Produção - `docker-compose.producao.yml`, em produção desde 22/10/2026 num
  VPS com Ubuntu 24.04 (ADR 0016). Nginx (front compilado, TLS do Let's
  Encrypt, `/api/` com WebSocket, `/q/`, `/l/` e `/webhooks/` para a API, sem
  versão no cabeçalho, gzip), certbot renovando, API, Worker, Gotenberg,
  `migrar`, Postgres e backup. Só o Nginx publica porta. Manual em
  [OPERACAO.md](OPERACAO.md).
- Leitura do QR durante a publicação - enquanto a API reinicia, `/q/` e `/l/`
  respondem 503 com uma página de espera que recarrega em 3 s, no lugar do
  502.
- A API confia nos cabeçalhos `X-Forwarded-*` de um salto
  (`Proxy:AtrasDoNginx`), para o limite por IP e o cookie `Secure`.
  Healthcheck pela própria API (`--saude`), porque a imagem `aspnet` não tem
  wget nem curl.
- Backup - `pg_dump` diário às 02:00 de Brasília, guardado por `BACKUP_DIAS`
  dias, com cópia por e-mail pelo Resend quando há `BACKUP_PARA`. O
  `backup.sh` grava em `.parcial` e só aceita a cópia que o
  `pg_restore --list` lê com os dados de `lancamentos`. O Deploy faz uma cópia
  antes de subir (`backup.sh agora`), e a cópia que falha segura a
  publicação.
- Modos de linha de comando da API - `--migrar` (com `ConnectionStrings__Dono`),
  `--demonstracao` (só em Development: operadora, pacotes, lotes, duas
  revendas, pedidos em cada etapa, equipe, vendas pagas e pendentes e
  leituras, tudo simulado), `--novo-revendedor`, `--nova-operadora`,
  `--trocar-email` (troca o e-mail de um login, com papel, revenda e
  histórico, e encerra as sessões dele), `--conferir-pagarme`,
  `--reservar-codigos`, `--importar-placas`, `--lotes-de-origem` e
  `--completar-clientes`. Uso no README e em [OPERACAO.md](OPERACAO.md).
- Desenvolvimento local - Postgres 17 do Homebrew; os testes criam e apagam o
  próprio banco.

## 11. CI e qualidade

- CI no GitHub Actions - restore, build em Release e a suíte inteira contra um
  Postgres 17 de serviço, a cada push no `main` e em pull request. Push só de
  documento (`docs/`, Markdown e `.claude/`) não roda o CI nem abre deploy.
  O front tem um job próprio no Node 24 LTS: `npm ci`, tipos, lint (oxlint,
  com as travas do design system), testes e build, mais o Storybook
  compilado. Os jobs `e2e` (Playwright com axe), `segredos` (gitleaks) e
  `qualidade` (SonarCloud) completam o CI.
- Documentação - em pull request, o job `documentacao` recusa mudança de
  código sem `docs/PDD.md` ou `docs/SDD.md`, salvo com o rótulo
  `sem-mudanca-de-doc`.
- Build trata aviso como erro, com `AnalysisMode=Recommended` e as regras de
  código sem uso (IDE0051, IDE0052, IDE0060, IDE0005) ligadas no
  `.editorconfig`; knip no front.
- Testes da API - xUnit v3 na Microsoft.Testing.Platform. Unidade (regras de
  cobrança, parcelamento, contrato, código da placa, destino, link de
  avaliação, ciclo da placa, lote, venda, comissão, documentos), contrato HTTP
  dos clientes contra servidor falso e fluxo completo no Postgres. O Pagar.me
  da API de teste é falso: nenhuma chamada sai para a rede. O fluxo
  financeiro roda com o Worker de relógio adiantado; os testes que rodam
  rotinas do Worker processam a fila inteira do banco dos testes.
- Os testes entram no Postgres com o papel `avalia_app_teste`, membro do
  `avalia_app`: as mesmas permissões e o mesmo RLS, sem trocar a senha que a
  API local usa. Teste de trava de segurança é conferido desligando a trava e
  vendo o teste falhar.
- SonarCloud - projeto `Hitice_Avalia-Sales`, analisado pelo CI desde
  25/10/2026. O job `qualidade` (`dotnet-sonarscanner` 11.3.0, C#,
  TypeScript e a cobertura dos testes da API pelo `dotnet-coverage`) roda com
  o segredo `SONAR_TOKEN` e as variáveis `SONAR_ORGANIZACAO` (`hitice`) e
  `SONAR_PROJETO` (`Hitice_Avalia-Sales`); sem o token, avisa e passa. O job
  roda em `continue-on-error`: não bloqueia o merge nem segura o deploy. O
  token é pessoal (a conta gratuita não tem token de organização) e vence em
  24/11/2026.
- Deploy - workflow `Deploy` no runner próprio do VPS, só depois do CI verde
  de um push no `main` deste repositório (`workflow_run.event`, `head_branch`
  e `head_repository`), com aprovação do ambiente `producao`. Pull request de
  fork de colaborador externo pede aprovação antes de rodar workflow. As
  imagens levam o commit (`VERSAO`); o Deploy faz o backup, sobe e confere
  `/api/health` pelo domínio. Voltar é publicar o commit anterior.
- Sentry - `Sentry.AspNetCore` e `Sentry.Extensions.Logging` 6.12.0 e
  `@sentry/react` 11.5.0, ligados só com DSN e sem dado pessoal; no front, o
  SDK é carregado à parte só quando há DSN. A versão publicada vai como
  `avalia-sales@<commit>` (`Sentry__Release`, `VITE_SENTRY_RELEASE`). A API
  ignora `RegraDeNegocioException` (422) e `OperationCanceledException`.
  Monitor de disponibilidade em `/api/health` a cada 60 s e check-in do
  Worker a cada 5 minutos (monitor `avalia-sales-worker`, alerta com 2
  faltas).

## 12. Decisões técnicas

Resumo. Contexto, opções e consequências de cada uma ficam nos ADRs, em
[docs/adr](adr/README.md), no formato MADR.

| Data | Decisão | Motivo |
| --- | --- | --- |
| 21/10/2026 | .NET 10 LTS | O .NET 8 sai de suporte em 10/11/2026; o código é o mesmo |
| 21/10/2026 | Tenant no banco único com filtro do EF Core e RLS | Duas travas independentes contra vazamento entre revendas |
| 21/10/2026 | Front único em React | Um design system, um build, um conjunto de componentes |
| 21/10/2026 | Split em `flat` calculado pela plataforma | `percentage` não aceita 1,99 |
| 21/10/2026 | Cobrança gravada antes de chamar o Pagar.me, com o id como `code` | A API não documenta idempotência; o código acha o pedido depois de uma queda |
| 21/10/2026 | Vendedor não é recebedor no Pagar.me | A revenda paga a comissão por Pix; a plataforma calcula o saldo |
| 21/10/2026 | Webhook só grava; o Worker reconsulta a fonte | Nenhum dos dois webhooks assina o corpo |
| 21/10/2026 | Vínculo externo sem RLS | O webhook chega sem revenda |
| 21/10/2026 | Token do front só em memória | Script injetado não lê memória do app; o refresh em cookie HttpOnly resolve a recarga |
| 21/10/2026 | Papel da operadora sem revenda e tabela marcada `IAtendidoPelaOperadora` | A operadora atende os pedidos de todos sem abrir as demais tabelas a ela |
| 21/10/2026 | `placas_emitidas` da plataforma, sem RLS, e `placas` da revenda | A leitura do QR descobre a revenda sem login; o destino e a venda ficam isolados |
| 21/10/2026 | Refresh token rotativo em cookie HttpOnly | O token de acesso continua só em memória, e recarregar a página não derruba a sessão |
| 21/10/2026 | Pagamento da venda lido da cobrança por origem | A cobrança gravada antes da queda do Pagar.me já aponta para a venda |
| 21/10/2026 | Simulador do Pagar.me sem estado, pelo UUID v7 | API e Worker são processos separados e precisam da mesma resposta |
| 21/10/2026 | Razão só de inserção, com estorno apontando para a linha | Mês fechado não muda; o mesmo pagamento não é lançado duas vezes |
| 21/10/2026 | Saldo de comissão lido do razão | Estorno depois do repasse vira saldo negativo e desconta no próximo |
| 21/10/2026 | Pedido de placas pelo checkout, sem split | O link só divide em cartão; o pacote é todo da Avalia |
| 21/10/2026 | Avisos ao vivo pelo LISTEN/NOTIFY do Postgres | API e Worker são processos separados; o NOTIFY sai no commit e não pede Redis |
| 21/10/2026 | Protheus por contrato REST próprio e fila | Sem ambiente público; a chave do pedido evita pedido de venda repetido |
| 21/10/2026 | Marca só nas variáveis do tema, contraste conferido nas duas pontas | Componente nenhum muda; a API não confia na tela |
| 21/10/2026 | Swagger UI sobre o OpenAPI nativo do ASP.NET Core | O documento é o do framework; o Swashbuckle entra só como página |
| 21/10/2026 | `@hookform/resolvers` 5.9 e não 6.0 | A 6.0.0 declara versões conflitantes de uma dependência opcional e não instala sem forçar |
| 21/10/2026 | Nenhuma biblioteca de idempotência pronta | A mais citada tinha 329 estrelas e nenhum commit desde 11/2024; o índice único do Postgres resolve a unicidade |
| 22/10/2026 | Contrato e distrato em PDF pelo Gotenberg, a partir de HTML | O layout aprovado é HTML; o Chromium fica isolado, sem rede (ADR 0018) |
| 22/10/2026 | Transferência de clientes por função e política de RLS próprias | Mudar a revenda de uma linha só dentro da função, sem depender de superusuário (ADR 0019) |
| 24/10/2026 | Mês fechado garantido por gatilho no banco, que passa a linha atrasada ao mês corrente | Linhas do razão nascem em vários pontos; o pagamento de fim de mês processado depois do fechamento não pode travar o Worker (ADR 0020) |
| 24/10/2026 | Custo da placa vendida e apropriação lidos do razão na DRE, e não lançados | A receita da venda já traz a venda e o estorno; nada a mais no caminho do pagamento |

## 13. Pendências técnicas

- Pagar.me - executar um estorno parcial real; conferir com a primeira venda
  real no cartão o formato do `/tokens`; conferir nos eventos de produção se
  toda cobrança repete o `code` do pedido (a reconsulta diária da cobrança
  paga cobre o evento que não casar) e se o pedido gerado pelo link de
  checkout leva o `order_code` no `code`; cadastro de recebedor pela API
  (`POST /recipients`, com `register_information` e KYC). A referência de 2026 se contradiz no prefixo
  do id (`re_` ou `rp_`), nos tipos (texto ou número) e no formato da data:
  escrever com o ambiente de testes. Até lá, a operadora informa o recebedor
  em Revendedores.
- D4Sign - passar do ambiente de testes para a conta de produção.
- Google Maps - conferir se `GOOGLE_ID_DO_MAPA` tem o Map ID próprio; sem
  ele, vale `DEMO_MAP_ID`, que o Google indica só para teste.
- Protheus - o serviço REST do lado do Protheus e `PROTHEUS_URL_BASE`,
  `PROTHEUS_USUARIO` e `PROTHEUS_SENHA` no `.env` de produção.
- Placas da Avalia - desligar o módulo de vendas do sistema anterior. O
  repasse do `/q/` de lá continua para sempre.
- Backup - cópia fora do VPS, criptografada, num bucket de armazenamento de
  objetos (recomendado o Backblaze B2, com 10 GB sem custo), e ensaio
  periódico de restauração.
- SonarCloud - desligar a análise automática no painel do SonarCloud, para
  valer só a análise do CI. Renovar o `SONAR_TOKEN` antes de 24/11/2026.
- Limite de memória e CPU por contêiner no Compose de produção, a definir com
  o tamanho do VPS.
- Pacote inicial do front com 460 kB em 23/10/2026 (React, Router, Query, Zod
  e o formulário de entrada); o polyfill de QR (44 kB e 1,1 MB de
  WebAssembly) só baixa no aparelho sem `BarcodeDetector`.

## 14. Registro de mudanças

| Data | Mudança |
| --- | --- |
| 25/10/2026 | Front do caixa - `financeiro/CartaoDoCaixa.tsx` (`useCaixa`, `useSalvarSaldoInicial`, `useExcluirSaldoInicial`), `mesSomado` em `fatias.ts`; tabela do Mapa com `tipo` e `AcoesDaLinha`. |
| 25/10/2026 | Tabela - `TableHead` com tipo da coluna (`texto`, `numero`, `data`, `situacao`, `acoes`), copiado para as células e aplicado pelo `index.css`; rolagem própria e cabeçalho fixo só a partir de `md`; `AcoesDaLinha` aceita `para` e `href`. |
| 25/10/2026 | Acessibilidade - área de rolagem sem `tabIndex` fixo (Sonar S6845); `useRegiaoRolavel` dá `tabindex`, `role="region"` e nome só quando a área rola e não tem nada focável (axe `scrollable-region-focusable`). |
| 25/10/2026 | Caixa - `Application/Financeiro/Caixa.cs`, rotas `GET/PUT/DELETE /financeiro/caixa`; tipos de lançamento `AporteDeSocio` e `SaldoInicialDoCaixa` (texto, sem migration), natureza `Aporte` na retirada, ações de auditoria `SaldoInicialDoCaixaDefinido` e `SaldoInicialDoCaixaExcluido`. O caixa soma as linhas de `Receita` e `AporteDeSocio` e tira `TaxaAvalia`, `ComissaoPaga`, `Despesa` e `RetiradaDeSocio` da competência do saldo inicial em diante, e os pedidos de placas pagos desde o início dela. `Dre.Carregar` reconhece em um mês a receita sem cobrança de venda `DoSistemaAnterior`, com o custo. `CaixaTest`. |
| 25/10/2026 | `Rodape` da entrada com `<img>` dos SVG oficiais em `web/public/parceiros` (`pagarme.svg` verde, `stone.svg`); `resend.svg` removido. |
| 25/10/2026 | Mapa - `enquadramento` corta 10% de cada ponta em latitude e longitude a partir de 8 lugares, `fitBounds` com 24 px e zoom máximo 16; marcadores guardados e retirados na troca. Deploy - o passo "Limpar imagens velhas" apaga as imagens `avalia-sales-*` com tag de commit, menos a versão no ar e a anterior, e o cache de build com mais de 7 dias. SonarCloud pelo CI (`SONAR_TOKEN`, `SONAR_ORGANIZACAO`, `SONAR_PROJETO` gravados; job `qualidade` com `continue-on-error`). CI sem script de instalação de dependência (`npm ci --ignore-scripts`) e sem `npx`: Playwright e Vite pelo `npm run`, na versão do `package-lock`; gitleaks-action fixada por SHA (v2.3.9); expressões regulares de e-mail sem retrocesso no front e no conversor do Sales; id da linha da Nova venda por `crypto.getRandomValues` fora de HTTPS; rodapé do PDF com `lang` e `title`, sem mudança no documento gerado. |
| 25/10/2026 | Documento revisto - texto reorganizado, estado das integrações e das fases atualizado. |
| 25/10/2026 | Revisão de arquitetura, segundo lote - Retomadas reconsultam a cobrança paga uma vez por dia (chargeback e estorno pelo painel do Pagar.me sem webhook, `ChargebackTest`); a renovação da sessão passa por `navigator.locks` e duas abas não gastam o mesmo refresh token; Nginx com `/q/` e `/l/` respondendo 503 com página de espera e recarga em 3 s enquanto a API reinicia, e log de acesso sem query string (`sem_query`); `backup.sh` só aceita a cópia que o `pg_restore --list` lê com os dados de `lancamentos`; Worker manda check-in ao Sentry a cada 5 minutos (monitor `avalia-sales-worker`, alerta com 2 faltas); pedido abortado por quem chamou responde 499 sem virar erro; monitor de disponibilidade do Sentry em `/api/health` a cada 60 s. |
| 24/10/2026 | 2.7.0 - `POST /clientes` (`Clientes.Cadastrar`, recusa CPF, CNPJ ou telefone já cadastrado; ação de auditoria `ClienteCadastrado`); `PUT /operadora/revendedores/{id}/renovacao` no lugar do `PUT /revenda/renovacao`; `GET /auditoria?busca=` (texto no alvo, código, antes e depois, nome da ação e nome do vendedor); `GravarMarca.CabecalhoNaCor`; migrations `CpfDoVendedor` (vendedores.cpf) e `CabecalhoNaCorDaMarca` (revendedores.cabecalho_na_cor), as duas aditivas. Front - `Table` com rolagem própria, cabeçalho fixo e busca pelo DOM a partir de 7 linhas (`ListaComBusca` para cartões); `Input` de senha com o olho; Sonner (`components/ui/sonner.tsx`) e `meta.sucesso` nas gravações (MutationCache no `main.tsx`); `VendasEm30Dias` compartilhado entre o início do revendedor, o do vendedor e as leituras da placa. `PainelDoVendedor` com `GET /painel-do-vendedor` (meta, vendido pago no mês de Brasília e ranking por vendas pagas) e `GET /carteira`; `CobrancaDaVenda.PixCopiaECola` só com o Pix pendente; migration `MetaDoVendedor` (vendedores.meta_mensal_cents, aditiva). |
| 24/10/2026 | Revisão de arquitetura, primeiro lote - o Deploy só roda para o CI de push no `main` deste repositório (`workflow_run.event`, `head_branch` e `head_repository`), porque o runner fica no VPS e o repositório é público; cópia do banco antes de subir (`backup.sh agora` devolve erro quando falha); cada rotina do Worker no próprio escopo e `try` (`Rotinas.Rodar`); a cobrança nasce dentro de uma transação com trava de aplicação pela origem (`IBancoDeDados.TravarOrigemDaCobranca`) e recusa a segunda cobrança viva da mesma venda; migration `RazaoSoDeInsercaoNoBanco` (REVOKE UPDATE, DELETE em `lancamentos` do papel da aplicação); gzip no Nginx; job `qualidade` do SonarCloud com C#, TypeScript e a cobertura dos testes da API (`dotnet-coverage`). |
| 24/10/2026 | `GET /vendas/{id}` (`Vendas.Detalhe`, `DetalheDaVenda`) - a venda com cliente, cobrança, itens do grupo, ordens de serviço e razão; o vendedor só abre a dele e não recebe o razão. Categoria de despesa `ServicoDePjComNota`. |
| 24/10/2026 | Sentry com a versão publicada: `Sentry__Release` e `VITE_SENTRY_RELEASE` recebem `avalia-sales@<commit>` do Compose (API, Worker e front), para o erro apontar a versão e o commit suspeito. |
| 24/10/2026 | Front - `Moldura` no computador vira painel lateral da altura da janela (grade de `15rem` e conteúdo até `max-w-6xl`): logo na célula do topo do cabeçalho, bloco da revenda e do papel, Nova venda como botão, grupos e o menu da conta (`MenuDaConta`) no pé; até o tablet nada muda. `MarcaAvalia` aceita `className`. História `Negócio/Moldura` no Storybook sem API (cache do TanStack Query e roteador em memória), com o parâmetro `semMargem` no `preview.tsx`. |
| 24/10/2026 | Financeiro do dono - rotas `/financeiro/dre`, `/historico`, `/meses`, `/fechamentos` (e `/{competencia}/reabertura`), `/despesas`, `/socios`, `/retiradas` (CRUD); casos de uso `Dre`, `Despesas`, `Socios`, `Fechamentos`, `CustoDasPlacas`; regra de apropriação em `Domain/Financeiro/Reconhecimento`. Migration `FinanceiroDoDono` (tabelas `despesas`, `socios`, `retiradas`, `fechamentos` com RLS, colunas `lancamentos.categoria` e `placa_id`, gatilho do mês fechado, e as perdas e cortesias já existentes lançadas no razão). Tela `web/src/financeiro`. |
| 24/10/2026 | `Cobrancas.Cancelar` traduz a recusa 4xx do Pagar.me (`412 action_forbidden, Saldo insuficiente`) em `RegraDeNegocioException` (422); 5xx e tempo esgotado seguem como erro. O Sentry da API ignora `RegraDeNegocioException` (422) e `OperationCanceledException` (pedido abortado por quem chamou). |
| 24/10/2026 | `GET /vendas/{id}/pagamento` (`PagamentoDaVenda`: situação e vencimento do Pix da cobrança mais recente), consultado a cada 5 s pela tela de resultado até pagar, falhar ou vencer. |
| 24/10/2026 | Testes que rodam rotinas do Worker (ERP, Retomadas, eventos) processam a fila inteira do banco dos testes: com limite de 50, o pedido do teste ficava atrás dos criados por outros testes e o CI falhava conforme a ordem. |
| 24/10/2026 | `GET/PUT/DELETE /clientes/{id}`, `POST /clientes/{id}/juncao`, `PUT /vendas/{id}` (`CorrigirVenda`), `POST/DELETE /operadora/revendedores/{id}/suspensao`, `DELETE /repasses/{id}`, `POST /operadora/servicos/{id}/reabrir`. Migration `SuspensaoRepasseDesfeito` (`revendedores.suspensa_em`, `motivo_da_suspensao`, `repasses.desfeito_em`). |
| 24/10/2026 | `IGatewayDePagamento.CancelarPedido` (GET /orders/{id} e DELETE /charges/{id} das pendentes e pagas); `Cobrancas.Cancelar`. `vendas.motivo_do_cancelamento`, `observacao_do_cancelamento`, `cancelada_em`, `teste_ate`, `teste_avisado_em`, `teste_expirado_em`, `teste_convertido_em` (migration `CancelamentoComMotivoETesteGratuito`, índice parcial nos testes em curso). `TestesGratuitos.Processar` roda nas Retomadas do Worker, revendedor por revendedor. `POST /vendas/{id}/cancelamento` com `motivo`, `observacao`, `placa`; `NovaVenda.testeGratuito`. |
| 24/10/2026 | `POST /vendas/{id}/troca-de-placa` passa à equipe, com `motivo`; `Placa.ReceberVendaDe(antiga, motivo, agora)` grava o motivo e a data na antiga. |
| 24/10/2026 | `lotes.titulo`, `categoria`, `arquivado_em`, `baixado_em` e `placas.motivo_da_baixa`, `baixada_em` (migration `LotesComCategoriaEBaixaComMotivo`; lote que já existia conta como baixado). Rotas `PUT/DELETE /operadora/lotes/{id}`, `PUT /operadora/lotes/{id}/arquivado` (lote livre), `GET /lotes` e `PUT /lotes/{id}/arquivado` (dono), `POST/DELETE /placas/{codigo}/baixa`. Comando `--lotes-de-origem` e `converter_do_sales.py --lotes`. |
| 24/10/2026 | `Telefones.Normalizar` tira o 55 (só com 12 ou 13 dígitos, porque 55 também é DDD) e o 0; migration `TelefoneSemPrefixo` normaliza clientes, vendedores e revendedores, passando o RLS revendedor a revendedor. `Renovacoes.BaseDaRenovacao`. `Impressao/ArteDoQr` e `Impressao/Png` (PNG RGBA sem biblioteca de imagem; a marca embutida em `Impressao/Marca`); `GET /operadora/lotes/{id}/impressao?formato=SvgEPng|Svg|Png`, CSV com CRLF. Mapa: cidade pelo `addressComponents` do Places, no navegador. |
| 23/10/2026 | Importação com `cliente_email` e `cliente_lugar_no_google`; `--completar-clientes` preenche os dois nos clientes já importados (só campo vazio), para o mapa achar os lugares. |
| 23/10/2026 | `--importar-placas` lê a data sem hora (meia-noite em Brasília) e a grava em UTC; antes o Postgres recusava o fuso -03:00 e o arquivo inteiro caía no salvamento. |
| 23/10/2026 | `converter_do_sales.py --no-ar-sem-venda-a-zero` - placa ativa ou suspensa sem venda no Sales entra como venda de R$ 0 na data do cadastro, sem vendedor; `--so-os-mapeados` deixa com o dono a venda de quem não está no `--vendedor`. |
| 23/10/2026 | CI do `main` ignora push só de documento (`paths-ignore`), e com isso o deploy dele não fica esperando aprovação. Estorno de venda cancela as ordens de serviço abertas dela (`EfeitosDaCobranca.Estornar`). |
| 23/10/2026 | `Parcelamento` (custo do Pix e do cartão de 1x a 12x); total do parcelado com o custo para o cliente e parte da Avalia líquida da tarifa no Pix e à vista; `custoDasParcelasBps` em `GET /pagamento/cartao`. Migration `PerfilDoGoogleComVintePorCento`. |
| 23/10/2026 | Parte da Avalia no serviço - `produtos_da_plataforma.desconto_da_revenda_bps` (2000; 1500 no `perfil-google-placa`) e `vendas.parte_da_avalia_cents` (migration `ParteDaAvaliaNosServicos`). A cobrança soma a parte à taxa em `taxa_avalia_cents`, que vai na regra da Avalia no split e no razão (por fora, a acertar); o piso do preço da revenda soma a parte. |
| 23/10/2026 | `revendedores.taxa_avalia_bps` passa a 0 em todas as revendas e no padrão da coluna (migration `TaxaDaAvaliaZerada`); com taxa zero o split leva uma regra só, da revenda. `Revendedor.DefinirTaxaAvalia` para a edição futura pela operadora. |
| 23/10/2026 | `pedidos_placas.retirada` (boolean, padrão falso) e `retirada` no corpo do despacho. Split com uma regra só quando o recebedor da revenda é o da Avalia. Comando `--conferir-pagarme` (`GET /recipients/{id}`). Logos das integrações em `web/public/parceiros`, mostrados como máscara na cor do texto. |
| 23/10/2026 | Front - `componentes/AcoesDaLinha` (ação principal e menu no computador, fileira de botões no celular), `sombra-de-rolagem` no contêiner da tabela, botões da célula quebram linha, layout com coluna lateral a partir de `xl`. Faixas de quantidade de pacote dos testes separadas das dos pacotes semeados (20, 50 e 100). |
| 23/10/2026 | Cadastro temporário de operadora removido (rotas `/cadastro/operadora`, `CadastroDeOperadora`, a opção `Plataforma:CadastroDeOperadoraAberto` e a variável do Deploy). A operadora é uma só e nasce pelo `--nova-operadora`. |
| 23/10/2026 | `POST /conta/senha` (`GestorDeSenhas.Trocar`), com limite da entrada e liberada na conta encerrada, como `/conta/segundo-fator`. |
| 23/10/2026 | Cadastro temporário de operadora - `GET /cadastro/operadora`, `POST /cadastro/operadora/codigo` (limite por IP) e `POST /cadastro/operadora`. Só e-mail do domínio `Plataforma:DominioDaOperadora`, código do provedor de e-mail do Identity, bloqueio por tentativas erradas. Fechado por padrão: abre só com `Plataforma:CadastroDeOperadoraAberto=true`, que em produção vem da variável `CADASTRO_DE_OPERADORA_ABERTO` do ambiente `producao` do GitHub. Cada operadora criada entra na auditoria da operadora. |
| 23/10/2026 | `Importacao.Importar` recusa revendedor com `encerrada_em`. |
| 23/10/2026 | Migration VendaDoSistemaAnterior (`vendas.do_sistema_anterior`), recusa em `Vendas` (cobrança, cancelamento, cortesia, recebido por fora). A comissão devida importada entra com a data da importação; a da venda vai na descrição. |
| 23/10/2026 | Versão 1.9.0. Conta encerrada só consulta também no front e sem `/lugares`; reabertura pedida pelo dono e aprovada ou recusada pela operadora (`revendedores.reabertura_pedida_em` e `reaberta_em`, migration ReaberturaDaConta, contrato novo depois da reabertura); `IContas.AtivarLogins`; comando `--trocar-email`. |
| 23/10/2026 | Documento revisado para a produção - stack, arquitetura, isolamento, front, modelo de dados numa tabela só, testes e pendências atualizados. |
| 23/10/2026 | Versão 1.8.2. Aba aberta antes de uma publicação pedia o arquivo da tela com o hash antigo ("Importing a module script failed"): as rotas carregadas sob demanda e o `vite:preloadError` recarregam a página uma vez (`lib/recarga.ts`, trava de 1 minuto contra laço), e a raiz das rotas tem `ErroDaTela` no lugar da tela de erro do React Router. |
| 23/10/2026 | Versão 1.8.1. `POST /conta/segundo-fator/desligar/codigo` manda código novo ao e-mail cadastrado (`GestorDeSegundoFator.PrepararDesligamento`); `desligar` aceita esse código, o do app ou um de recuperação. `notificacoes.usuario_id` (migration NotificacaoParaUsuario) e `Notificacoes.ParaVendedor`; o sino (`/notificacoes`, política `Todos`) separa operadora, dono (sem destinatário ou ele) e vendedor (só ele). |
| 23/10/2026 | Versão 1.8.0. Auditoria da operadora - middleware `AuditoriaDaOperadoraNaRota` no grupo `/operadora` grava cada escrita com resposta abaixo de 400 (corpo JSON com buffer; arquivo não), migration AuditoriaDaOperadora, rotas `GET /operadora/auditoria` e `/operadora/auditoria/exportar`, tela da operadora. `components/ui/toggle-group.tsx` (ToggleGroup do Radix com a cara das abas) no lugar de abas sem painel, que o axe recusava (`aria-valid-attr-value`). Teste de acessibilidade cobre as telas da operadora. |
| 23/10/2026 | Versão 1.7.3. Reconsulta da assinatura (Worker a cada 30 minutos e botão do dono), `EfeitosDaAssinatura` comum ao webhook e à reconsulta, com trava da linha do contrato; aviso ao dono no sino. `PUT /conta/dados/empresa` aceita endereço sem celular quando a revenda não tem um. Ficha da placa transferida monta a venda com o cliente e o valor gravados na transferência (a venda ficou na revenda antiga). Conta encerrada ainda marca notificação como lida. Teste de ponta a ponta do encerramento; história do endereço no Storybook. 253 testes na API. |
| 22/10/2026 | Versão 1.7.2. Representante e cargo da Avalia vêm das variáveis do ambiente `producao` no GitHub, pelo job do Deploy, e não do `.env` do VPS. |
| 22/10/2026 | Versão 1.7.0. Migration EncerramentoDeConta (endereço e encerramento em `revendedores`; transferência em `placas`; políticas e função de transferência). Contrato versão 2 e distrato gerados no Worker pelo Gotenberg (serviço `gotenberg` no Compose, `Pdf:UrlDoConversor`); PDF fixo da versão 1 removido do assembly. BrasilAPI no cadastro. Rotas do encerramento e da exportação, middleware `ContaEncerrada`, `IAssinaturaEletronica.Cancelar`. `RevendedorDto` com `temEndereco`, `encerramentoPedidoEm` e `encerradaEm`. 250 testes na API. |
| 22/10/2026 | `infra/migracao/converter_do_sales.py` (só biblioteca padrão; backup em SQLite na memória). Importador com `produto` (chave do catálogo), `produto_preco_cents` e `comissao_devida_cents` (lançamento ComissaoDevida com a data da venda). |
| 22/10/2026 | `docs/contrato` - estilo comum em `documento.css` (serifada, campos em branco, assinaturas e testemunhas) e `gerar.mjs` gera o PDF de cada HTML ao lado dele, sem tocar no PDF embutido em `Recursos`, que segue na versão 1. |
| 22/10/2026 | Versão 1.6.2. Busca do Google (`BuscaNoGoogle`) sem `<form>` próprio: dentro do formulário do link, o envio nativo do navegador recarregava a página antes de chamar `/lugares`; o Enter no campo busca. Serviços em `/links?aba=link|nfc|maps|status`; `encurtador` e `acompanhar` ainda abrem a aba certa (notificações gravadas). Notificações de ordem de serviço apontam para `/links?aba=status`. |
| 22/10/2026 | Versão 1.6.1. D4Sign - `sendtosigner` idempotente (o 400 "already in the signature phase" conta como enviado; o contrato da revenda ficava preso em Rascunho depois de 5 tentativas); `resend` com a `key_signer` de `documents/{uuid}/list` e `download` (URL temporária, só de *.d4sign.com.br) conferidos em docapi.d4sign.com.br. Rotas do dono `/revenda/contrato`, `/revenda/contrato/reenvio` e `/revenda/contrato/pdf`. Migration ContratoJaEmAssinatura - `contratos.reenviado_em` e volta à retomada do contrato preso. Sino no celular com margem de colisão. |
| 22/10/2026 | Versão 1.6.0. Gráficos com Recharts 3.10.1 (componente `chart` do shadcn), série em `--chart-1` (roxo do traço na plataforma; tom 45 do destaque na revenda, pela faixa de luminosidade do validador da skill dataviz). Convite do vendedor 24 horas. Catálogo não mostra o custo ao vendedor (`pisoCents` zero). Migration FotoDoQrDinamico (só dados). Rota de tela `/painel/configuracoes`; `/painel/servicos` leva a `/links?aba=acompanhar`. |
| 22/10/2026 | Versão 1.5.2. Link curto `/l/` repassado à API pelo Nginx e pelo Vite, como o `/q/` (caía no app e pedia login). Busca de lugares com o caminho `./places:searchText`: sem o `./`, o .NET lia `places:` como esquema e a chamada não saía; teste prende a URL. |
| 22/10/2026 | Versão 1.5.1. A demonstração só cria o pacote de 10 placas: 20, 50 e 100 vêm da migration, e criar de novo quebrava o índice único no banco novo do e2e. |
| 22/10/2026 | Versão 1.5.0. Migration CatalogoServicosENotificacoes - `produtos_da_plataforma` com os 8 produtos e as fotos em `web/public/produtos`, pacotes 20, 50 e 100 a R$ 23,90, 16,90 e 13,90 a placa, `produtos.produto_da_plataforma_id`, `vendas.placa_id` opcional, `pedidos_placas.producao_em`, `ordens_de_servico` e `notificacoes` com RLS. Rotas `/catalogo` (sai `/produtos`; a foto em `/catalogo/{id}/foto` é pública), `/operadora/catalogo`, `/servicos`, `/operadora/servicos`, `/operadora/pedidos/{id}/producao` e `/notificacoes`. A venda aceita o id do catálogo e cria a linha da revenda na primeira vez; serviço sem placa nem destino; a venda paga de serviço vira ordem (EfeitosDaCobranca). |
| 22/10/2026 | Versão 1.4.0. `MeusDados` com `GET/PUT /conta/dados`, `PUT /conta/dados/empresa` (dono, auditado como EmpresaAlterada) e `PUT /conta/dados/recebimento` (vendedor); sai `PUT /revenda/celular`. Auditoria com filtro por ação, 20 por página e `GET /auditoria/exportar`. Menu da conta (`dropdown-menu` do shadcn) no cabeçalho. |
| 22/10/2026 | Versão 1.3.0. Tabela `chamados` e `mensagens.chamado_id` (migration Chamados), rotas `/chamados` e `/operadora/chamados` no lugar de `/chat`, prazo de 24 horas úteis em `HorarioDeAtendimento`, e-mail de chamado novo para a operadora e de resposta para o dono. |
| 22/10/2026 | Versão 1.2.0. Front - menu lateral em grupos com ícones, fundo do item ativo do tom 95 da primária (`variaveisDaMarca` em `marca.ts`, a mesma conta na prévia da Marca), seleção de texto e foco sólido na cor da marca, foco inicial do modal no próprio modal, barra de carregando no topo e blocos de carregando (`Carregando`, `CarregandoTela`), recorte do logo com `react-easy-crop` 6.2.4, Links em abas. Contrato embutido na versão 1 aprovada. |
| 22/10/2026 | E-mail com logo da plataforma em PNG, faixa no traço da marca, botão com o rótulo da linha `Rótulo: endereço` e código em destaque; textos de convite, senha, confirmação, aparelho novo e pedidos no formato novo. |
| 22/10/2026 | Versão 1.1.0. A versão do app mora em `web/package.json` (semver: correção sobe o último número, funcionalidade o do meio) e aparece no rodapé. |
| 22/10/2026 | Coluna `celular` em `revendedores`: obrigatória no `POST /cadastro`, trocada pelo dono em `PUT /revenda/celular`, no `RevendedorDto` da operadora e da revenda. |
| 22/10/2026 | Confirmação do e-mail também por código de 6 dígitos (`hash_do_codigo`, `erros_do_codigo` em `links_de_senha`) e reenvio só depois de 1 minuto. Fila de e-mail com `proxima_tentativa_em`, espera dobrando até 6 horas e 20 tentativas. Front - máscaras de CNPJ, CPF, telefone e código (`lib/mascaras.ts`), contorno do campo com o traço da marca no foco e verde quando confere, logo Avalia Sales em SVG (`public/marca`) e favicon do gauge. `.gitignore` com `.claude/settings.local.json` e `.claude/worktrees/`. |
| 22/10/2026 | Produção no ar em sales.avaliaone.com.br (VPS com Ubuntu 24.04). Backup às 02:00 de Brasília, com cópia por e-mail pelo Resend (`BACKUP_PARA`); o `backup.sh` não estava no Git por causa de uma regra do `.gitignore`. |
| 22/10/2026 | Dono sem e-mail confirmado só abre `/conta` (claim `email_confirmado` nas políticas Dono e Equipe; o painel mostra só a confirmação). Segundo fator também por código no e-mail (`EmailTokenProvider`, `usuarios.segundo_fator_por_email`, `/auth/entrar/codigo/reenvio`). Aviso de aparelho novo por e-mail (`aparelhos_conhecidos`, cookie `avalia_aparelho` de 2 anos, só o hash). Entrada clara com o logo da AvaliaOne e rodapé com direitos e versão; `Indicador` e `Dados` no design system; tabela no celular em linhas de rótulo e valor. Demonstração com cinco logins e senha `demo-123`; teste de ponta a ponta da jornada entre operadora, revenda e vendedora, com isolamento. |
| 22/10/2026 | Storybook 10 (`web/.storybook`, histórias ao lado dos componentes, addon a11y, marca da plataforma e de revenda na barra), compilado no CI. |
| 22/10/2026 | Revisão pelas skills `seguranca` e `codigo-morto` - CSP no Nginx, gitleaks no CI, knip no front; removidos `radio-group`, esquemas e apelidos sem uso, `Mensagem.MarcarLida` e `CodigoDaPlaca.RestricaoDaRota`. Renovação aberta sem Pix vivo deixa de travar cancelamento e troca de placa: é cancelada na hora. |
| 22/10/2026 | Cartão no balcão - tokenização no navegador (`lib/cartao.ts`, Luhn e validade), `GET /pagamento/cartao`, `Pagarme:ChavePublica`, cartão no simulador; testes de ponta a ponta com cartão aprovado e recusado. |
| 22/10/2026 | Segundo fator - `GestorDeSegundoFator`, `AuthenticatorTokenProvider`, rotas `/auth/entrar/codigo` e `/conta/segundo-fator`, tela Segurança, passo do código na entrada; teste de ponta a ponta liga o fator e entra com o código. |
| 22/10/2026 | Auditoria (`RegistroDeAuditoria`, `Auditoria`, migration Auditoria), registrada nos casos de uso de placa, venda, equipe, comissão, produto, repasse, marca, links e LGPD; `FichaDto.Historico`; tela Auditoria. 195 testes na API. |
| 22/10/2026 | `produtos.comissao_bps` (migration ComissaoDoProduto); `Vendas.QuemVende` resolve vendedor, produto e padrão. |
| 22/10/2026 | Testes de ponta a ponta com Playwright e axe, job `e2e` no CI. O axe achou e foram corrigidos: contraste da aba inativa e telas sem `h1` (entrada, cadastro, senha, catálogo). |
| 22/10/2026 | Formulários de cadastro em React Hook Form + Zod com `Campo` e `lib/esquemas.ts` (Nova venda, Equipe, Links, senha, Pacotes, Cupons); o de Pacotes não limpa mais o que foi digitado quando a gravação falha. |
| 22/10/2026 | Contrato da API versionado em `web/openapi.json` (ContratoDaApiTest falha se divergir e regrava fora do CI); tipos do front gerados dele com openapi-typescript (`npm run tipos`, `src/api/esquema.d.ts`), conferidos no CI. JSON da API com número estrito; papel do perfil como enumeração. |
| 22/10/2026 | Front - `Table` rotula as células pelo cabeçalho e vira cartão abaixo de `md`; menu lateral a partir de `lg`, de cima com quebra em `md`, barra de baixo no celular; `--traco`, `--noite` e Inter Display (`public/fonts/inter-display.woff2`) nos títulos. |
| 22/10/2026 | Renovação da sessão fora do limite da entrada (`LimiteDeRenovacao`, `Limites:RenovacoesPorMinuto`); a renovação no front só limpa a sessão no 401. Vários usuários no mesmo IP eram derrubados ao abrir páginas em sequência. |
| 22/10/2026 | Confirmação de e-mail do dono, remetente e resposta por revenda, HTML com a marca (`ModeloDeEmail`), migration EmailConfirmadoERemetente (contas antigas marcadas como confirmadas). NFC no iPhone pelo NFC Tools. |
| 22/10/2026 | Front - barra de baixo no celular (`PRINCIPAIS` na `Moldura`, o resto no "Mais"), menu de cima só a partir de `md`; token `--sucesso`; CPF, e-mail e telefone conferidos no navegador (`documentos.ts`); `destino.ts` confere o link do Google como a API; trocar de passo limpa a recusa da tentativa anterior. 42 testes no front. |
| 22/10/2026 | Encurtador - `links` (RLS) e `codigos_de_link` (plataforma, sem RLS, como `placas_emitidas`), rotas `/links` e `GET/HEAD /l/{codigo}` com 302 sem cache, `Links.DoNegocio` na transação da venda, `VendaRegistrada.LinkCurto`. Destino recusa `/l/` da plataforma. Nova venda na ordem negócio, placa, cliente, cobrança. 184 testes na API. |
| 22/10/2026 | Convite do vendedor e esqueci a senha (`POST /equipe/{id}/convite`, `POST /auth/senha/link`, `POST /auth/senha`, `GestorDeSenhas`, migration LinksDeSenha), telas `/convite`, `/senha` e `/esqueci`. Grade de duas colunas sem rolagem lateral no celular. 181 testes na API. |
| 22/10/2026 | Revisão do texto das telas e das recusas da API (`RegraDeNegocioException`), mais curtas. |
| 22/10/2026 | Renovação recebida por fora (`/placas/{codigo}/renovacao/recebimento`). Logo reduzido no navegador (`reduzirLogo`, canvas para WebP em até 512 px); `chamarResposta` só marca JSON quando o corpo é texto. 177 testes na API, 38 no front. |
| 22/10/2026 | Cancelamento, cortesia, troca de placa e recebido por fora (rotas `/vendas/{id}/cancelamento`, `/cortesia`, `/troca-de-placa`, `/recebimento`), migration VendaDesfeita, diálogo de ações na tela Vendas. 176 testes. |
| 21/10/2026 | LGPD - exportação e anonimização do cliente, tela Clientes. 172 testes. |
| 21/10/2026 | Comandos `--reservar-codigos` e `--importar-placas` (CSV pelo leitor do .NET, colunas pelo nome, tudo ou nada com `--ensaio`), fábricas de importação no Domain (placa, venda, cliente, lote sem o limite de tiragem). 170 testes. |
| 21/10/2026 | Produção - compose com Nginx e certbot, backup diário, healthcheck pela API, cabeçalhos encaminhados, workflow de deploy no runner próprio com aprovação, SonarCloud opcional no CI, Sentry opcional na API, Worker e front, manual de operação. Moldura do front carregada depois do login. |
| 21/10/2026 | Mapa dos lugares vendidos (Google Maps JavaScript com lista quando falta a chave) e chat entre revendedor e operadora, com a regra de gravação da operadora nas tabelas atendidas. |
| 21/10/2026 | White label - marca do revendedor (nome, cores e logo em tabela própria com RLS), rotas `/marca` e `/revenda/marca`, tela Marca com prévia, cores aplicadas nas variáveis do tema. |
| 21/10/2026 | Fase 3, primeira parte - avisos ao vivo (SignalR com LISTEN/NOTIFY), Protheus por contrato REST e simulador com fila no Worker, tela do ERP da operadora. |
| 21/10/2026 | Fase 2 - checkout do Pagar.me para o pedido de placas, cupons, razão, saldo e repasse de comissão, renovação, efeitos do pagamento, telas de cupons, comissões, razão e renovação. Front em HTTPS na rede local para testar Web NFC e câmera no celular, com `/q/` passando pelo Vite. |
| 21/10/2026 | Telas da Fase 1 - operadora (pedidos, lotes com download da impressão, pacotes, revendedores), dono (início, vendas com nova cobrança, estoque com consignação, pedidos de placas com ativação de lote, equipe e comissão, catálogo com editar, desativar e excluir), balcão (Nova venda com QR, NFC e Pix, ficha da placa) e cadastro. Sessão restaurada pelo cookie ao recarregar. Endereço da placa nas respostas de venda e ficha, para a gravação do NFC. |
| 21/10/2026 | Fase 1 na API - tabelas da plataforma e do revendedor, papéis e políticas, sessão com refresh em cookie, rotas por área, leitura do QR, impressão do lote, Resend, Places, simulador do Pagar.me, conferência de cobrança pendente, contrato automático e demonstração completa. Testes com papel próprio no Postgres. Página de rotas trocada do Scalar pelo Swagger UI. |
| 21/10/2026 | Comando `--novo-revendedor` para a operadora cadastrar revendedor e dono pela linha de comando. |
| 21/10/2026 | Front em `web/`: entrada, painel protegido e catálogo, tema da plataforma com contraste conferido, toque de 44 px no celular, 20 testes e job no CI. |
| 21/10/2026 | Página de rotas (OpenAPI e Scalar em `/rotas`) e comando `--demonstracao`, os dois só em Development. |
| 21/10/2026 | Documento criado. Fase 0 (camadas, login, isolamento, CI) e integrações do Pagar.me e da D4Sign com caixa de entrada, Worker e retomadas. |
