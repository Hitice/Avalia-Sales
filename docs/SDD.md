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
| Banco | PostgreSQL com EF Core e Npgsql | Postgres 17, EF Core 10 | Em produção |
| Acesso | ASP.NET Identity e JWT (HMAC-SHA256) | ASP.NET Core 10 | Em produção |
| Integrações | Pagar.me v5, D4Sign v1, Resend, Google Places (New), BrasilAPI (CNPJ e CEP), Gotenberg (PDF) | Gotenberg 8 | Em produção; a D4Sign no ambiente de testes |
| ERP | Protheus por contrato REST próprio | - | Conexão pronta; desligada sem `Protheus:UrlBase` |
| QR da impressão | QRCoder (MIT), SVG e PNG sem System.Drawing | 1 | Em produção |
| Resiliência | Microsoft.Extensions.Http.Resilience (Polly) | 10 | Em produção |
| Tempo real | SignalR, com o LISTEN/NOTIFY do Postgres levando os avisos do Worker à API | ASP.NET Core 10, `@microsoft/signalr` 10 | Em produção |
| Front | React 19 + Vite, React Router, TanStack Query, Zustand, React Hook Form + Zod, Tailwind 4, shadcn/ui (Radix), Recharts, Sonner | React 19, Vite 8, Router 8, Tailwind 4, Zod 4, TypeScript 6, Recharts 3 | Em produção |
| Leitura de QR e Pix no front | `BarcodeDetector` nativo; sem ele, o polyfill `barcode-detector` sobre o ZXing C++ em WebAssembly (`zxing-wasm`), servido pelo app; `qrcode` para o QR do Pix | - | Em produção |
| Mapa | Google Maps JavaScript pelo `@googlemaps/js-api-loader`, marcador avançado | loader 2 | Em produção |
| Documentação da API | OpenAPI do ASP.NET Core em `/openapi/v1.json` e Swagger UI em `/swagger` | ASP.NET Core 10, Swashbuckle.AspNetCore.SwaggerUI 10 | Só em Development |
| Infra | Linux, Docker Compose, Nginx, Let's Encrypt (certbot) | nginx 1.28, postgres 17 | Em produção no VPS |
| CI e CD | GitHub Actions (runner próprio só no Deploy), SonarCloud, Sentry | - | Em uso |

A tabela leva só a versão maior. A exata de cada pacote mora em
`Directory.Build.props` e `Directory.Packages.props`; as do front, em
`web/package.json`; as das imagens, no `docker-compose.producao.yml` e nos
Dockerfiles.

## 2. Arquitetura

```
Front (React) ──┐
Página do QR ───┼── Nginx ── Api ──┬── PostgreSQL 17
                │                  ├── Pagar.me, D4Sign, Resend, Google Places, BrasilAPI
Webhooks ───────┘   Worker ────────┴── Gotenberg (PDF), Protheus
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

### Listagens paginadas

Toda rota GET que devolve uma lista que cresce com o uso responde no mesmo
formato, com o limite aplicado no servidor (`Application/Pagina.cs`):

```json
{ "itens": [ ... ], "total": 230, "proxima": 2 }
```

- Parâmetros: `pagina` (a partir de 1) e `tamanho`. Sem `tamanho`, 50; acima
  de 100, vale 100; abaixo de 1, 50; `pagina` abaixo de 1 vale 1. Valor fora
  da faixa é corrigido, e não recusado. Página depois da última devolve
  `itens` vazio, o `total` certo e `proxima` nulo.
- `total` é o do filtro (`busca` e filtros da rota), e não só da página.
  `proxima` é o número da próxima página, ou nulo na última.
- Ordem total: o último critério é sempre o `Id`, para o Postgres não
  repetir nem pular item entre uma página e a outra quando o horário empata.
- `busca` (texto livre) ignora caixa e acento ("otica" acha "Ótica"), pela função
  `avalia_normalizar` do banco sobre a extensão `unaccent`, criada pela migration `BuscaSemAcento`
  com o dono do banco; `%` e `_` valem como letra. Vale antes da página. O front manda o termo à API; a busca local da tabela fica ligada só
  em tabela que não é paginada (`<Table semBusca>` nas outras).
- Rotas: `/vendas`, `/clientes`, `/carteira`, `/placas`, `/lotes`, `/pedidos`,
  `/chamados`, `/links`, `/servicos`, `/repasses`, `/razao` (os lançamentos
  do mês, em `lancamentos`), `/financeiro/despesas`, `/financeiro/retiradas`,
  `/auditoria`, e da operadora `/operadora/pedidos`, `/lotes`, `/erp`,
  `/cupons`, `/revendedores`, `/servicos`, `/chamados`, `/auditoria`,
  `/operadora/financeiro/receita-por-revenda`, `/despesas` e `/custos-de-lote`.
  `/placas` e `/operadora/lotes` trazem campos a mais (`porEstado`,
  `arquivados`). `GET /chamados/{id}` devolve as mensagens mais novas e
  `antes={idDaMensagemMaisAntiga}` busca as anteriores (`haAnteriores`).
- Números que a tela mostra sobre a lista inteira vêm de rota própria, e não
  da soma da página: `/vendas/resumo`, `/operadora/chamados/resumo`,
  `?situacao=AFazer&tamanho=1` em `/servicos`.
- Não paginam, por serem limitadas pelo desenho: `/notificacoes` (as 30 mais
  recentes e a contagem exata de não lidas), `/equipe`, `/pacotes`,
  `/catalogo`, `/financeiro/socios`, `/financeiro/meses` e os selos de
  mês, `/razao/meses`, `/mapa` (tem tela própria).

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
- Financeiro da operadora - `lancamentos_da_operadora`, `despesas_da_operadora`,
  `custos_de_lote` e `fechamentos_da_operadora` não têm `revendedor_id`, e a política
  de RLS delas nomeia a operadora (`app.operadora = 'sim'`). O livro também aceita
  a inserção da revenda de origem (`revendedor_origem_id` igual ao `app.revendedor_id`
  da sessão), para o pagamento e a ativação do lote lançarem na transação deles; a revenda
  insere a própria linha e não lê nenhuma. `fechamentos_da_operadora` é de leitura aberta,
  porque o gatilho do livro a lê na sessão de quem lança. Nenhum UPDATE nem DELETE no
  livro (ADR 0025).
- Leitura do grupo - para o resultado consolidado, `IEscopoDeRevenda` (`EscopoDeRevenda`)
  abre uma sessão com a revenda marcada (`revendedores.do_grupo_da_operadora`) e devolve só
  números somados. Confere a marca no banco a cada leitura, recusa a revenda não marcada
  mesmo com o id certo e só serve a quem entra como operadora (ADR 0025).
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
  as telas do revendedor e da operadora. Os fluxos rodam a 390 px; o
  `acessibilidade.spec.ts` roda a 390 px e a 1440 px. Cada tela passa no axe
  (WCAG 2.2 AA; violação séria ou crítica falha) e sem rolagem lateral.
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
| `revendedores` | A revenda - nome, CNPJ, celular (só dígitos, com DDD), endereço (`endereco_logradouro`, `_numero`, `_complemento`, `_bairro`, `_cidade`, `_uf`, `_cep`), recebedor no Pagar.me (`recebedor_pagarme`), `taxa_avalia_bps` (sem uso desde 27/10/2026: o código não lê nem grava, e a coluna sai numa migration posterior; até lá o EF a mapeia como propriedade de sombra), comissão padrão (`comissao_padrao_bps`, 2000 = 20%), renovação (`renovacao_bps`, 6000 = 60%), marca (`nome_exibido`, `cor_primaria`, `cor_destaque`, `cabecalho_na_cor`, `marca_alterada_em`), `contrato_assinado_em`, encerramento (`encerramento_pedido_em`, `encerrada_em`, `reabertura_pedida_em`, `reaberta_em`), suspensão (`suspensa_em`, `motivo_da_suspensao`), liberação de destinos do dia (`destinos_liberados`, `destinos_liberados_em`) a marca de revenda do grupo da operadora (`do_grupo_da_operadora`, `grupo_definido_em`, `grupo_definido_por`) e a marca de revenda receptora no encerramento (`receptora_no_encerramento`) | Sem RLS; CNPJ único; no máximo uma receptora (índice único parcial) |
| `usuarios`, `papeis` e demais do Identity | Login, `papel` (Operadora, Dono, Vendedor), `ativo`, revenda e `segundo_fator_por_email` | Sem RLS; e-mail único na plataforma; check: operadora sem revenda, os outros com |
| `sessoes` | Refresh token pelo hash SHA-256, família do login, validade de 14 dias | Sem RLS; hash único |
| `links_de_senha` | Convite, redefinição e confirmação de e-mail: hash do token, finalidade, validade, `hash_do_codigo` e `erros_do_codigo` | Sem RLS |
| `aparelhos_conhecidos` | Hash do aparelho por usuário, primeiro e último acesso | Sem RLS |
| `vendedores` | Equipe, com o mesmo id do login: telefone, CPF, chave Pix, comissão própria, meta do mês (`meta_mensal_cents`), ativo | RLS |
| `produtos_da_plataforma` | Catálogo da Avalia - chave (única), nome, descrição, tipo (Placa, PlacaComServico, Servico), preço sugerido, desconto da revenda (`desconto_da_revenda_bps`, padrão 2000), pedido mínimo (`quantidade_minima`), situação (Disponivel, Esgotado, ForaDeLinha), ordem, foto padrão do front (`/produtos/*.jpg`) e foto enviada (bytea até 512 KB, tipo e versão) | Sem RLS |
| `produtos` | Preço da revenda para um produto do catálogo (`produto_da_plataforma_id`, único por revenda) e comissão do produto (`comissao_bps`). O produto sem catálogo é o de antes dele, só para as vendas antigas | RLS |
| `pacotes` | Quantidade e preço oferecidos pela operadora | Sem RLS; quantidade única |
| `cupons` | Código, desconto (pontos base ou centavos), validade, limite e usos | Sem RLS; código único; travado na hora do uso |
| `pedidos_placas` | Pacote copiado (quantidade, `preco_do_pacote_cents`), cupom, desconto, valor, situação, pagamento, `producao_em`, despacho (envio ou `retirada`, com `transportadora` e `codigo_de_rastreio`), lote, ativação e os lembretes de ativação (`lembretes_de_ativacao`, `operadora_avisada_da_ativacao_em`) | RLS com a operadora |
| `lotes` | Número, quantidade, título, categoria, pedido e revenda do despacho, ativação, `arquivado_em` e `baixado_em` (primeiro download) | Sem RLS; número e pedido únicos |
| `placas_emitidas` | Código (chave), sequência da plataforma, lote, revenda depois da ativação | Sem RLS; só identificadores (ADR 0007) |
| `codigos_reservados` | Código que existe fora da plataforma e a origem | Sem RLS; o sorteio pula |
| `placas` | Placa no estoque da revenda: custo congelado, situação, consignação, destino, venda, vencimento, leituras, aviso do vencimento (`vencimento_avisado`, `janela_avisada`), baixa (`motivo_da_baixa`, `baixada_em`) e a transferência no encerramento (`transferida_em`, `cliente_da_transferencia_id`, `valor_da_venda_cents`) | RLS; código único; políticas `transferencia_no_encerramento` (UPDATE) e `_leitura` (SELECT), também em `clientes` e `links` |
| `leituras` | Leituras por placa e dia | RLS; chave (placa, dia), soma por `on conflict` |
| `clientes` | Negócio que comprou: nome, responsável, documento, telefone, e-mail, lugar no Google (só o id), `anonimizado_em` | RLS; documento único por revenda |
| `vendas` | Placa (nula na venda de serviço), cliente, produto, vendedor, quantidade, grupo (`grupo_id`), valor, tabela, custo, parte da Avalia (`parte_da_avalia_cents`), comissão, cortesia, repasse que pagou a comissão, cancelamento (`motivo_do_cancelamento`, `observacao_do_cancelamento`, `cancelada_em`), teste gratuito (`teste_ate`, `teste_avisado_em`, `teste_expirado_em`, `teste_convertido_em`) e `do_sistema_anterior` | RLS; uma venda registrada por placa (o índice único de `placa_id` só vale para venda registrada, e a placa da venda cancelada vende de novo); índice parcial nos testes em curso |
| `cobrancas` | Cobrança: origem (venda, pedido de placas, renovação) e id da origem, meio, valor, o que o split manda à Avalia (`taxa_avalia_cents`, `Cobranca.ParteDaAvaliaCents` no código: a parte dela nos serviços, ou o pedido de placas inteiro; zero na venda de placa e na renovação), `estornado_cents`, situação, pedido e link no Pagar.me, URL do checkout, Pix (copia e cola, QR, vencimento), `conferida_em` | RLS; `pedido_no_gateway` único; parte da Avalia copiada na criação |
| `renovacoes` | Placa, venda, valor, situação, de, até, paga em | RLS; uma aberta por placa |
| `lancamentos` | Razão da revenda: tipo (`Receita`, `TaxaAvalia` - a parte da Avalia nos serviços; a linha anterior a 27/10/2026 pode ser da taxa da plataforma, que saiu -, `ComissaoDevida`, `ComissaoPaga`, `CustoDaPlaca`, `Perda`, `Despesa`, `RetiradaDeSocio`, `AporteDeSocio`, `SaldoInicialDoCaixa`), origem, venda, vendedor, valor com sinal, competência, `categoria` (da despesa ou natureza da retirada), `placa_id` e a linha estornada (`estorna_id`) | RLS; só inserção, com `REVOKE UPDATE, DELETE` do papel da aplicação (ADR 0011); único (origem, tipo, venda, vendedor), com nulos iguais, na linha original e único `estorna_id` no estorno; gatilho `lancamento_em_mes_aberto` passa ao mês corrente a linha de mês fechado (ADR 0020). Despesa, retirada e perda usam origem nova por versão, para o índice aceitar a correção |
| `lancamentos_da_operadora` | Livro da operadora: tipo (`TaxaDaPlataforma` - só histórico, não gravado desde 27/10/2026; o estorno de linha antiga sai com ele -, `ParteDaAvaliaNosServicos`, `PedidoDePlacas`, `Despesa`), revenda de origem (`revendedor_origem_id`, nula na despesa), origem (cobrança, pedido ou versão da despesa), venda, valor com sinal, competência, `por_fora`, `categoria` e a linha estornada (`estorna_id`). O Id da parte da Avalia é o da linha `TaxaAvalia` do razão que ela espelha | RLS só da operadora, com inserção também da revenda de origem; só inserção (`REVOKE UPDATE, DELETE`); único (origem, tipo, venda), com nulos iguais, na linha original e único `estorna_id` no estorno; gatilho `lancamento_da_operadora_em_mes_aberto` (ADR 0025) |
| `despesas_da_operadora` | Data, descrição, categoria (`GraficaEMaterial`, `ExecucaoDeServico`, `Frete`, `SoftwareEFerramentas`, `Impostos`, `Outros`, `TarifaDoGateway`), valor, fornecedor (`fornecedor_id`, opcional) e a linha do livro em vigor | RLS só da operadora |
| `fornecedores` | Nome, documento (CNPJ ou CPF, único), contato, funções (bits: Gráfica 1, Acrílico 2, Confecção 4, Logística 8, Outros 16), observação, ativo | RLS só da operadora; despesa e custo de lote apontam para ele (`Restrict`) |
| `acertos_de_revenda` | Quitação do a acertar: revenda, valor, data e observação (ADR 0028) | RLS: a operadora lê e escreve; a revenda só lê os dela |
| `destinos_bloqueados` | Domínio bloqueado como destino de QR e de link curto, motivo, quem e quando (ADR 0026) | Leitura aberta, escrita só da operadora; domínio único |
| `fechamentos_da_operadora` | Mês do livro fechado: competência, fechado em e por, reaberto em e por, motivo | Leitura aberta, escrita só da operadora; um em vigor por mês |
| `custos_de_lote` | Custo real de produção do lote: gráfica, acrílico, quantidade e fornecedor (opcional); o custo por placa é o total pela quantidade | RLS só da operadora; chave é o lote, apagada com ele |
| `repasses` | Comissão paga ao vendedor: valor, vendas, data de corte, comprovante, `desfeito_em` | RLS; vendedor travado na gravação |
| `despesas` | Data, descrição, categoria, valor e a linha do razão em vigor (`lancamento_id`) | RLS |
| `socios` | Sócio do contrato social: nome e CPF opcional | RLS; não se exclui com retirada |
| `retiradas` | Sócio, data, natureza, valor, observação e a linha do razão em vigor | RLS |
| `fechamentos` | Mês fechado: competência, fechado em e por, reaberto em e por, motivo | RLS; um em vigor por mês (índice único com `reaberto_em is null`) |
| `contratos` | Tipo (`ContratoDoRevendedor`, `Distrato`), PDF, hash SHA-256, signatário, documento na D4Sign, etapa do envio, tentativas, situação, `conferido_em`, `reenviado_em` | RLS; `documento_na_assinatura` único |
| `vinculos_externos` | Código no Pagar.me ou documento na D4Sign, e a revenda dona | Sem RLS; chave (origem, id_externo); só identificadores |
| `eventos_externos` | Webhook cru, chave do vínculo, tentativas, próxima tentativa, último erro, processado em | Sem RLS; único (origem, id_externo) |
| `emails` | Fila de e-mails com chave única, nome do remetente, resposta, cor da marca, tentativas, próxima tentativa e id no Resend; no e-mail `sensivel`, o assunto vira `(conteúdo protegido)` e o texto guarda assunto e texto cifrados (ver ADR 0024) | Sem RLS |
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
- Split - regras `flat` em centavos: a revenda e, quando há parte da Avalia
  no serviço, a Avalia (ADR 0003). Não há taxa percentual sobre a venda:
  venda de placa e renovação vão inteiras para a revenda. A revenda tem
  `liable`, `charge_processing_fee` e `charge_remainder_fee`. Regra de valor
  zero não é enviada. Revenda com o recebedor igual ao da Avalia vai numa
  regra só, com o valor inteiro, porque o split não repete recebedor; a parte
  da Avalia continua gravada na cobrança e no razão. Os recebedores aceitam
  `re_` e `rp_`.
- Checkout do pedido de placas - `POST /paymentlinks` com `type` order,
  `order_code` igual ao id da cobrança, `max_paid_sessions` 1, validade de 3
  dias (`expires_in` em minutos), Pix, cartão e boleto, sem `split_settings`:
  o pedido de placas é todo da Avalia (ADR 0012). O pedido no Pagar.me só
  nasce quando a revenda paga. A conferência de cobrança pendente procura pelo
  código; link vencido sem pagamento vira cobrança cancelada, e outro link
  pode sair.
- Cancelamento e estorno - `IGatewayDePagamento.CancelarPedido` lê o pedido
  (`GET /orders/{id}`) e chama `DELETE /charges/{id}` em cada cobrança
  pendente ou paga. Link ainda sem pagamento não tem pedido lá:
  `Cobrancas.Cancelar` chama `CancelarLink` (`PATCH
  /paymentlinks/{id}/cancel`, sem corpo, 204), e o checkout deixa de abrir. `EstornarParte` faz o mesmo `DELETE` com `amount`, para o
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
  Em Production, a API e o Worker não sobem com a URL do sandbox sem
  `D4Sign__SandboxEmProducao=true` (`ValidadorDasOpcoesD4Sign`): a variável
  esquecida não leva a produção ao sandbox em silêncio.
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
  `Plataforma:ExigirSegundoFatorDaOperadora` (padrão ligado) exige o segundo
  fator da conta da operadora nas ações de dinheiro (ADR 0027).

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
- O HTML (`ModeloDeEmail`) sai do texto escapado e dos destaques que o
  servidor marcou: quem enfileira passa em `destaques` os endereços e códigos
  que ele montou, e `DestaquesDoEmail` guarda o hash deles numa marca no fim
  do texto, depois de tudo o que veio do usuário (a última marca vale; vai
  cifrada junto no e-mail sensível). A linha `Rótulo: https://...` com
  endereço marcado vira botão com esse rótulo, na cor da marca, com a letra
  escolhida pelo contraste WCAG; a linha `Rótulo: 123456` com código marcado
  mostra o código em destaque; só endereço marcado é clicável. Texto do
  usuário (nome do cadastro, mensagem de chamado) nunca vira botão nem link. E-mail da plataforma leva o logo em PNG
  de 96 px (`web/public/marca/avaliasales-email.png`, pelo
  `Plataforma:UrlDoPainel`) e a faixa de 3 px no traço da marca. Texto em
  15/24 px, peso 400, cinza escuro; botão de 14 px. Rodapé fora do cartão -
  "Mensagem automática da Avalia Sales." ou "Enviado por {revenda} pela
  plataforma Avalia Sales." (a resposta vai ao dono), e o endereço da
  plataforma. Quem escreve e-mail novo usa essas duas linhas, sem HTML, e não
  põe id interno no texto (usa quantidade, revenda e o código do Pagar.me).
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
  `Protheus__Senha`. Sem endereço, a integração fica desligada: `FilaDoErp`
  não grava nada, o Worker não envia e a tela ERP da operadora avisa
  (`GET /operadora/erp/integracao`, `{ ligada }`). Em produção o endereço
  ainda não está configurado. O que já estava na fila espera o endereço.

## 7. Ambientes de teste e simuladores

Conferido nas páginas oficiais em 21/10/2026.

| Serviço | Ambiente de teste | Limites e ressalvas |
| --- | --- | --- |
| Pagar.me | Chaves `sk_test_` e `pk_test_` no mesmo endereço da produção | Pix simulado aprova até R$ 500,00 e falha acima; o simulador de Pix não funciona com split, então o teste de split usa cartão (`4000000000000010` aprova, `4000000000000028` recusa). Split exige conta PSP; a conta da Avalia é marketplace |
| D4Sign | `sandbox.d4sign.com.br`, cadastro próprio | Sem validade jurídica; mesmo limite de 10 requisições por hora até a D4Sign aumentar |
| Resend | Endereços de teste `delivered@resend.dev`, `bounced@resend.dev`, `complained@resend.dev` | Plano gratuito com 3.000 e-mails por mês e 100 por dia; o teste conta na cota |
| Google Maps e Places | Sem ambiente de testes: toda chamada conta na cota | Cota gratuita por SKU por mês (10.000 carregamentos de mapa dinâmico, 10.000 Place Details Essentials); exige conta de faturamento no Google Cloud |
| Protheus | Sem ambiente público | Simulador próprio, só em Development; os testes usam um dublê (`ErpFalso`) |
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
4. A falha soma tentativa, guarda o erro e adia a próxima em
   `proxima_tentativa_em` (30 s, dobrando, até 2 horas: as 10 tentativas
   cobrem cerca de 4 horas). Na décima o evento sai da fila e a operadora
   recebe um e-mail (chave `evento-desistido:<id>`, na mesma transação); a
   conferência das cobranças e dos contratos nas Retomadas aplica o que a
   fonte disser. Evento de pedido ou documento que não é da plataforma é
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
- Aviso do vencimento (`AvisosDeVencimento.Processar`) - placa ativa, com venda
  de pé e fora de teste, a 30 e a 7 dias do vencimento: sino do dono e do
  vendedor que vendeu, e e-mail ao cliente em nome da revenda. A placa guarda o
  vencimento avisado e a menor janela já avisada; renovar muda o vencimento e
  recomeça a série.
- Lembrete de ativação (`PedidosDePlacas.LembrarDaAtivacao`) - pedido
  despachado e não ativado: lembrete ao dono a cada 7 dias, até 3, e aviso à
  operadora aos 30 dias.
- Fim do prazo de exportação - passados 30 dias do encerramento, desliga o
  login do revendedor.

### Efeitos do pagamento

- `EfeitosDaCobranca` roda quando a situação da cobrança muda, antes do mesmo
  `SaveChanges`: pedido de placas pago (com a entrada na fila do ERP), razão
  da venda paga (receita, parte da Avalia no serviço, comissão devida), venda paga de serviço
  virando ordem de serviço, renovação estendendo a placa, estorno com as
  linhas contrárias.
- Estorno (`EfeitosDaCobranca.Estornar`) - cancela as ordens de serviço
  abertas da venda e avisa a operadora da ordem entregue.
- A cobrança nasce dentro de uma transação com trava de aplicação pela origem
  (`IBancoDeDados.TravarOrigemDaCobranca`) e recusa a segunda cobrança viva
  da mesma venda.
- Recebido por fora (`MeioDePagamento.Dinheiro` e `OutroPix`) é uma cobrança
  que nasce paga, sem pedido no Pagar.me, e passa pelos mesmos efeitos no
  mesmo `SaveChanges`; a linha da parte da Avalia diz "a acertar". Desfazer leva a
  cobrança a estornada, e os efeitos lançam o contrário.
- Cancelamento (`Vendas.Cancelar`) - a venda se cancela antes da cobrança,
  depois cada cobrança viva é cancelada ou estornada (`Cobrancas.Cancelar`,
  `Cobrancas.EstornarVenda` no grupo), e as notificações da venda são
  encerradas (`notificacoes.venda_id`).
- Troca de placa - trava a placa nova (`FOR UPDATE`) e encerra a antiga com o
  motivo (`Placa.ReceberVendaDe`).
- Livro da operadora (`LivroDaOperadora`) - o mesmo `SaveChanges` que lança a linha
  `TaxaAvalia` no razão lança a linha do livro (`ParteDaAvaliaNosServicos`; a linha antiga da
  taxa da plataforma, pelo começo da descrição, é estornada como `TaxaDaPlataforma`), com o Id
  da linha do razão; o estorno do razão estorna a do livro
  (`EfeitosDaCobranca.Estornar` e `Cobrancas.EstornarVenda`). `PedidosDePlacas.Ativar`
  lança o pedido pago como receita da operadora. Tudo na sessão da revenda, que só insere.
- Custo da placa vendida e apropriação da receita são lidos do razão na DRE
  (`Dre`, `Domain/Financeiro/Reconhecimento`), e não lançados.

### Financeiro da operadora

- Rotas (política `operadora`, todas sob `/operadora/financeiro`): `GET /resultado?competencia=`
  (`ResultadoDoGrupo.DoMes`), `GET /receita-por-revenda` (paginada), `GET /meses`,
  `POST /fechamentos`, `POST /fechamentos/{competencia}/reabertura`, `GET/POST /despesas` e
  `PUT/DELETE /despesas/{id}`, `GET/POST /custos-de-lote` e `PUT/DELETE /custos-de-lote/{loteId}`.
  A marca é `PUT /operadora/revendedores/{id}/grupo`. A trilha da operadora registra as escritas.
- Casos de uso: `LivroDaOperadora`, `DespesasDaOperadora`, `FechamentosDaOperadora`,
  `CustosDeLote`, `ResultadoDoGrupo`. O `Dre` recebe o `TimeProvider` e não depende mais de
  `Fechamentos`, para rodar numa sessão que não é do usuário da requisição.
- `ResultadoDoGrupo` junta o livro do mês (separado pela marca do grupo), o `Dre` de cada revenda
  marcada, lido pelo `IEscopoDeRevenda`, e o custo dos lotes. Cada linha traz as quatro colunas; a
  conciliação lê a parte da Avalia e o pedido de placas do razão da revenda e do livro; o
  `TaxaDaPlataforma` antigo soma na parte da Avalia, nos dois lados. O custo real da placa vendida usa `Reconhecimento` com o custo por placa do lote. A
  receita por revenda lista só o que cada uma paga à operadora.
- Migration `FinanceiroDaOperadora`: tabelas, colunas de `revendedores`, políticas, gatilho e o
  backfill (`BackfillDoLivroDaOperadora`), idempotente.

### Encerramento da conta

- Rotas do revendedor - `GET/POST/DELETE /revenda/encerramento`,
  `POST /revenda/encerramento/reenvio`, `GET /revenda/encerramento/pdf` e
  `GET /revenda/exportacao` (JSON com clientes, vendas, placas e razão).
- Distrato assinado (webhook e reconsulta, como o contrato) aplica
  `EfeitosDoEncerramento` na transação do evento: `encerrada_em`, baixa do
  estoque, vendedores e logins de vendedor desligados e
  `transferir_clientes_da_revenda` para a revenda receptora. Distrato
  cancelado na D4Sign desfaz o pedido.
- Revenda receptora (ADR 0029) - marca `revendedores.receptora_no_encerramento`,
  dada pela operadora em `PUT /operadora/revendedores/{id}/receptora`
  (`{ receptora }`, segundo fator pela ADR 0027, trilha da operadora). Marcar
  uma tira a marca da anterior na mesma transação, com trava de aplicação;
  revenda encerrada ou com encerramento pedido não vira receptora; tirar a
  marca com encerramento em andamento é recusado. Sem receptora, o pedido de
  encerramento é recusado, e o distrato assinado sem ela não encerra: o evento
  volta para a fila com "Nenhuma revenda receptora. Marque uma em
  Revendedores." e encerra quando houver uma. A receptora não pede o próprio
  encerramento.
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
| Operadora - trocar nome, CNPJ e recebedor de revenda, registrar pagamento por fora, estornar pedido e registrar acerto exigem o segundo fator ligado (`AcoesSensiveisDaOperadora`); a troca de recebedor avisa o dono no sino e por e-mail (ADR 0027) | Em produção |
| Destino de QR e de link - até 10 cadastros novos por dia por revenda (contados na auditoria) e lista de domínios bloqueados, consultada ao cadastrar e a cada leitura e clique (ADR 0026) | Em produção |
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
  dias no disco, em claro; cópia cifrada no bucket (Cloudflare R2) e, com
  `BACKUP_PARA`, por e-mail pelo Resend. O `backup.sh` grava em `.parcial` e só
  aceita a cópia que o `pg_restore --list` lê com os dados de `lancamentos`. O
  Deploy faz uma cópia antes de subir (`backup.sh agora`); com o contêiner
  `backup` de pé, a cópia que falha segura a publicação, e com ele parado o
  Deploy só avisa. Cópia que falha e disco acima de `DISCO_ALERTA` mandam
  e-mail (conferidos uma vez por dia, só com `BACKUP_PARA`, `BACKUP_SENHA` e o
  Resend); falha do bucket fica só no log.
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
  compilado. Os jobs `e2e` (Playwright com axe), `vulnerabilidades` (NuGet e
  npm de produção), `segredos` (gitleaks), `documentacao` e `qualidade`
  (SonarCloud) completam o CI.
- Documentação - no push ao `main` e em pull request, o job `documentacao`
  recusa mudança de código sem `docs/PDD.md` ou `docs/SDD.md`, salvo com
  `[sem-doc]` na mensagem do commit ou o rótulo `sem-mudanca-de-doc` no pull
  request.
- Build trata aviso como erro, com `AnalysisMode=Recommended` e as regras de
  código sem uso (IDE0051, IDE0052, IDE0060, IDE0005) ligadas no
  `.editorconfig`. O knip do front roda à mão (skill `codigo-morto`), fora do
  CI.
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
- Sentry - `Sentry.AspNetCore`, `Sentry.Extensions.Logging` e
  `@sentry/react`, ligados só com DSN e sem dado pessoal; no front, o
  SDK é carregado à parte só quando há DSN. A versão publicada vai como
  `avalia-sales@<commit>` (`Sentry__Release`, `VITE_SENTRY_RELEASE`). A API
  ignora `RegraDeNegocioException` (422) e `OperationCanceledException`.
  Monitor de disponibilidade em `/api/health` a cada 60 s, configurado no
  painel do Sentry e fora do repositório, e check-in do Worker a cada 5
  minutos (`Rotinas.cs`, monitor `avalia-sales-worker`, alerta com 2 faltas).

## 12. Decisões técnicas

Resumo. Contexto, opções e consequências de cada uma ficam nos ADRs, em
[docs/adr](adr/README.md), no formato MADR.

| Data | Decisão | Motivo |
| --- | --- | --- |
| 21/10/2026 | .NET 10 LTS | O .NET 8 sai de suporte em 10/11/2026; o código é o mesmo |
| 21/10/2026 | Tenant no banco único com filtro do EF Core e RLS | Duas travas independentes contra vazamento entre revendas |
| 21/10/2026 | Front único em React | Um design system, um build, um conjunto de componentes |
| 21/10/2026 | Split em `flat` calculado pela plataforma | `percentage` não aceita centavo; desde 27/10/2026 leva só a parte da Avalia no serviço |
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
| 22/10/2026 | Contrato e distrato em PDF pelo Gotenberg, a partir de HTML | O layout aprovado é HTML; o Chromium não abre endereço externo nem interno (ADR 0018) |
| 22/10/2026 | Transferência de clientes por função e política de RLS próprias | Mudar a revenda de uma linha só dentro da função, sem depender de superusuário (ADR 0019) |
| 24/10/2026 | Mês fechado garantido por gatilho no banco, que passa a linha atrasada ao mês corrente | Linhas do razão nascem em vários pontos; o pagamento de fim de mês processado depois do fechamento não pode travar o Worker (ADR 0020) |
| 24/10/2026 | Custo da placa vendida e apropriação lidos do razão na DRE, e não lançados | A receita da venda já traz a venda e o estorno; nada a mais no caminho do pagamento |
| 26/10/2026 | Livro próprio da operadora e leitura restrita das revendas marcadas como do grupo | A operadora não tem revenda e não lê o razão das outras; o consolidado lê só agregados da revenda marcada (ADR 0025) |
| 27/10/2026 | Revenda receptora no encerramento por marca explícita, uma só | O CNPJ em configuração divergia do cadastro sem aviso; nenhuma regra pergunta qual revenda é a Avalia (ADR 0029) |
| 27/10/2026 | Fila do ERP desligada sem `Protheus:UrlBase` | Sem serviço do Protheus, a fila crescia sem destino (ADR 0014) |

## 13. Pendências técnicas

- Pagar.me - estorno parcial real não executado (o formato de `canceled_amount` segue sem prova; a plataforma só o lê); conferir com a primeira venda
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
- Backup - o envio ao bucket está ligado e a cópia do disco foi restaurada num banco à parte em
  26/10/2026. Falta o ensaio com o `.dump.enc` baixado do bucket (decifrar e restaurar), repetido todo mês.
- SonarCloud - renovar o `SONAR_TOKEN` antes de 24/11/2026.
- Pacote inicial do front com 460 kB em 23/10/2026 (React, Router, Query, Zod
  e o formulário de entrada); o polyfill de QR (44 kB e 1,1 MB de
  WebAssembly) só baixa no aparelho sem `BarcodeDetector`.

## 14. Registro de mudanças

As mudanças de 21 a 24/10/2026 estão em [historico/sdd-ate-24-10.md](historico/sdd-ate-24-10.md). Daqui em diante, linha nova no topo desta tabela.

| Data | Mudança |
| --- | --- |
| 27/10/2026 | Conexões do compose de produção com `GSS Encryption Mode=Disable` - o Npgsql 10 tenta a cifra GSSAPI por padrão, a imagem `aspnet` não tem a `libgssapi_krb5` e o banco é alcançado pela rede interna do Docker, sem Kerberos. |
| 27/10/2026 | Travas de dinheiro, concorrência e e-mail - `CustosDeLote` recusa criar, editar e excluir custo de lote ativado até um mês fechado da operadora; o limite diário de destinos é conferido sob a trava dos destinos da revenda (`TravarLinksDaRevenda`, reentrante), dentro da transação da venda e da troca de destino, o que também põe em fila o "um teste por cliente"; `Placa.SuspensaPorTeste` (migration `SuspensaoPorTesteEEsperaDosEventos`, com a marca das placas fora do ar pelo teste e a volta para fora do ar das religadas pelo botão) e `Alternar` recusa a placa assim; `eventos_externos.proxima_tentativa_em` com espera exponencial e e-mail à operadora na desistência; acréscimo do parcelamento rateado pelo valor de cada venda do grupo (só pagamento novo; o estorno usa o que foi lançado); o telefone só acha cliente quando um dos dois não tem documento, e sem documento na venda vale o cadastro sem documento ou o único documento do telefone; `IdempotenciaDaRequisicao` grava a chave mesmo com o cliente desconectado, e a cobrança depois do commit da venda não usa o cancelamento da requisição; e-mail de cobrança paga depois de cancelada com a origem, a revenda e o pedido no Pagar.me; `DestaquesDoEmail` (botão, código e link só do que o servidor marcou) e o cadastro público recusa nome com endereço de site; link de cadastro cifrado na fila (`sensivel`); ligar e desligar o segundo fator contam erro para o bloqueio; Worker - tempo esgotado do HttpClient é falha da rotina (filtro pela parada pedida), o conversor de PDF devolve `IntegracaoException`, e-mails antes das retomadas; auditoria da operadora grava sem o cancelamento da requisição e loga a falha; `ValidadorDasOpcoesD4Sign`. Front - chave de idempotência por tentativa de cobrança na Nova venda (`balcao/tentativa.ts`), `Protegida` renova o token vencido pelo cookie, marca e logo não somem quando o painel da marca fecha, câmera desligada quando o leitor falha, Sentry sem consulta nas URLs, Início e gráfico de 30 dias no dia de Brasília; a ficha da placa conta a janela de leituras do dia de Brasília. |
| 27/10/2026 | Modelo de e-mail mais leve - texto 15 px peso 400, logo de 96 px, faixa de 3 px, cartão com borda, rodapé com a origem e o endereço da plataforma. E-mails da operadora sobre pedido estornado ou pago de novo sem o id interno e sem a situação crua: trazem quantidade, revenda e o pedido no Pagar.me. |
| 27/10/2026 | Job `semgrep` no CI (regras abertas p/default, csharp, typescript, react, github-actions e dockerfile; falha em severidade ERROR, `.semgrepignore` para o que não é código de produção, `nosemgrep` com motivo nos falsos positivos conferidos). Ações do GitHub fixadas por commit (checkout v7.0.1, setup-dotnet v6.0.0, setup-node v7.1.0, upload-artifact v7.0.2). |
| 27/10/2026 | Revisão antes do push - cobrança cancelada que o Pagar.me confirma paga avisa a operadora para devolver (`EfeitosDaCobranca.PagaDepoisDeCancelada`, chave `cobranca:<id>:paga-depois-de-cancelada`); distrato assinado sem receptora não derruba a rodada das `Retomadas` (marca a conferência e segue); o Início fecha pela receita reconhecida, com perdas; destino antigo que não normaliza continua redirecionando (a recusa vale no cadastro); `destinoDaVolta` valida pelo parser de URL e recusa caractere de controle. Fica para depois - o aviso sem vínculo usa a chave do evento (order.paid e charge.paid do mesmo pagamento geram dois e-mails) e o `error_log` do Nginx ainda grava a URL do webhook em erro de upstream. |
| 27/10/2026 | Revisão de segurança e dinheiro - ligar o segundo fator encerra as outras sessões do login, mantida a de quem ligou; ligar, desligar e o desligamento pelo suporte (`--desligar-segundo-fator`, com o motivo) vão à auditoria da revenda ou à trilha da operadora; destino bloqueado com host normalizado (IDN, minúsculas, sem `www.` nem ponto final), host que não normaliza recusado no cadastro e bloqueado na leitura; excluir acerto, trocar a renovação e marcar o grupo pedem o segundo fator; registrar acerto trava a revenda (`pg_advisory_xact_lock`); o distrato calcula o devido por fora pelo razão (parte da Avalia das cobranças por fora, com estornos); revenda suspensa não vira receptora, receptora suspensa não conta e a receptora não é suspensa; tipo da linha do livro sem depender da descrição (só a taxa antiga da plataforma é `TaxaDaPlataforma`); leitura do grupo em sessão somente leitura (`default_transaction_read_only`); pagamento do Pagar.me sem vínculo avisa a operadora por e-mail; hub de avisos cai quando o token vence (`CloseOnAuthenticationExpiration`); `Idempotency-key` com o código da cobrança no POST de pedido e de link do Pagar.me; Nginx sem log de acesso em `/webhooks/`. |
| 27/10/2026 | Revisão do documento contra o código e o CI - tabela da stack só com a versão maior; Gotenberg sem a promessa de rede isolada (o Compose não isola, o Chromium bloqueia endereço); backup com o bucket, a condição do contêiner de pé e o alerta só por e-mail configurado; CI com `vulnerabilidades` e `documentacao` no push; knip fora do CI; monitor de `/api/health` configurado no painel do Sentry; ponta a ponta a 390 px, axe também a 1440 px; seção Listagens paginadas trazida do `OPERACAO.md`. |
| 27/10/2026 | `Protegida` manda para `/entrar?volta=<tela pedida>` e a entrada volta para ela depois do login, só com caminho interno (`destinoDaVolta`). Limpeza - saem `CustosDeLote.Pendentes`, `IContas.PedirConfirmacao` e `ProdutoDaPlataforma.DefinirFotoPadrao` (Roslynator), o clone do `Importar.cs` (`RevendedorEPlacas`) e os 9 apontamentos de manutenção do SonarCloud (regex gerada, testes sem asserção, `Program` público implícito); `financeiro/MesDoFinanceiro.tsx` (seletor de mês, fechamento) e `financeiro/DespesasDoMes.tsx` servem à revenda e à operadora, e a tabela de despesas da revenda ganha a `Rolagem`. |
| 27/10/2026 | Taxa da plataforma excluída (ADR 0003 revista) - saem `Revendedor.TaxaAvaliaBps` e `DefinirTaxaAvalia`, `RevendedorDto.taxaAvaliaBps` e `DivisaoDaCobranca`; `Cobranca` recebe só a parte da Avalia (`Cobranca.ParteDaAvaliaCents`, mapeada na coluna `taxa_avalia_cents`; o pedido de placas passa o valor inteiro). `EfeitosDaCobranca` rateia a parte só entre as vendas de serviço e a renovação não lança linha da Avalia. `LivroDaOperadora.ParteDaAvalia` e `ParteDaAvaliaEstornada` no lugar de `Taxa` e `TaxaEstornada`. `DreDoMes.parteDaAvaliaCents` (era `taxaCents`), `ResumoDoMes.parteDaAvaliaCents` e `CobrancaDto.parteDaAvaliaCents` (eram `taxaAvaliaCents`), `Conciliacao.DevidoPorForaCents`; Resultado do grupo sem as linhas `taxa-da-plataforma`, `taxa-da-plataforma-paga` e a conciliação dela, `ReceitaDaRevenda` sem `taxaDaPlataformaCents`, e o `TaxaDaPlataforma` antigo somado na parte da Avalia. `revendedores.taxa_avalia_bps` fica como propriedade de sombra do EF até a migration que a remove. Contrato e distrato sem taxa percentual. Testes - `Venda_de_placa_paga_nao_manda_nada_para_a_avalia_no_split_nem_lanca_parte_da_avalia`, `Linha_antiga_da_taxa_da_plataforma_soma_na_parte_da_Avalia_e_o_estorno_sai_com_o_tipo_dela`; os da taxa passam a provar a parte da Avalia. |
| 27/10/2026 | Revenda receptora (ADR 0029) - coluna `revendedores.receptora_no_encerramento` com índice único parcial (migration `RevendaReceptora`, que marca a revenda de CNPJ 39914870000101), `PUT /operadora/revendedores/{id}/receptora` com segundo fator, `Revendedores.DefinirReceptora`, `IBancoDeDados.TravarReceptora`, `RevendedorDto.receptoraNoEncerramento`. `EfeitosDoEncerramento` usa a marca e recusa sem ela; `Encerramento.Pedir` recusa sem receptora e a receptora não encerra. Sai `Plataforma:CnpjDaRevendaDaAvalia`. Tela Revendedores com o selo "Receptora" e "Marcar como receptora"/"Tirar de receptora". `RevendaReceptoraTest`, `Sem_receptora_o_pedido_e_recusado_e_o_distrato_assinado_espera_a_marca`. |
| 27/10/2026 | ERP - sem `Protheus:UrlBase`, `FilaDoErp.Enfileirar` não grava (a integração fica desligada); `GET /operadora/erp/integracao` e o aviso na tela ERP. A API de teste sobe com endereço (o Worker de teste usa o `ErpFalso`); `FluxoDoErpTest` tira o endereço numa API derivada. |
| 27/10/2026 | Contêiner `backup` alerta por e-mail a cópia que falha e o disco acima de `DISCO_ALERTA` (85%); comando `--desligar-segundo-fator --email= --motivo=` para recuperar a conta sem os códigos (encerra sessões e avisa o e-mail); job `documentacao` do CI também no push ao `main`, com `[sem-doc]` para refatoração; hook `PreToolUse` `migration-aditiva.sh` recusa editar migration já commitada; regras por pasta em `.claude/rules` e agente `revisor` em `.claude/agents`; testes de régua de texto (`TextoDasMensagensTest` no C#, selo de até duas palavras no Vitest). Selos curtos: Carência, Descontinuado, Para retirar, Cancelada, Teste vencido, Importada, Por fora. |
| 27/10/2026 | Log do Docker com teto de 5 arquivos de 10 MB por serviço (`x-registro` no compose de produção); job `vulnerabilidades` no CI (NuGet com transitivas e npm de produção, falha em alta ou crítica), sem Dependabot para não abrir branch; a trava de documentação do CI e do hook passa a cobrar também o `docker-compose.producao.yml`; `CancelarLink` no gateway (`PATCH /paymentlinks/{id}/cancel`) usado pelo cancelamento do pedido de placas; tela de entrada só com o Pagar.me. |
| 27/10/2026 | `AcoesDaLinha` mostra a principal em botão `sm` e o resto no "⋯"; coluna `acoes` presa à direita por `position: sticky` (`index.css`); `CardAction` centra e desconta a altura do botão `sm`; `financeiro/LinhaDaDre` compartilhada pelo Financeiro e pelo Início (`useDre` do mês corrente); menu lateral recolhido em `localStorage` (`avalia.menu-recolhido`, só no navegador, nada na API); `PedidosDePlacas.Cancelar` cancela as cobranças pendentes pelo `Cobrancas.Cancelar` em vez de recusar; `knip` ignora `public/now` (referência por HTML) e saem `useVender`, `temPlaca` e três tipos sem uso. Teste `Pedido_com_link_aberto_pode_ser_cancelado`. |
| 26/10/2026 | `GestorDeSegundoFator` enfileira "Segundo fator ligado" e "Segundo fator desligado" para o e-mail confirmado do dono (chave `segundo-fator-<mudança>:<usuário>:<segundo>`, sem dado sensível), na mesma gravação da auditoria; o e-mail do código ganha o aviso de não repassar. Teste `Ligar_e_desligar_o_segundo_fator_avisam_o_email_do_dono`. |
| 26/10/2026 | `Fornecedores` e `fornecedores`, `fornecedor_id` na despesa e no custo de lote, `transportadora` e `codigo_de_rastreio` no pedido (`FornecedoresEEntrega`); `PedidosDePlacas.Estornar` e `LembrarDaAtivacao` (`EstornoELembretesDoPedido`); `AcertosDeRevenda` e `acertos_de_revenda` (`AcertosDeRevenda`, ADR 0028); `AvisosDeVencimento` e colunas de aviso na placa (`AvisoDeVencimentoDaPlaca`); `ConfirmacoesDePagamento` (e-mail ao dono a cada pagamento); distrato desconta o acerto. |
| 26/10/2026 | `LimiteDeDestinos` e `DestinosBloqueados` (`LimiteDeDestinos`, `DestinosBloqueados`, ADR 0026): `GET /limite-de-destinos`, `POST /revenda/limite-de-destinos/solicitacao`, `POST /operadora/revendedores/{id}/liberacao-de-destinos` e `/operadora/destinos-bloqueados`; a leitura do QR e do link consulta a lista. `AcoesSensiveisDaOperadora` e `Plataforma:ExigirSegundoFatorDaOperadora` (ADR 0027). `PUT /operadora/revendedores/{id}/cadastro` corrige nome e CNPJ. |
| 26/10/2026 | Teste gratuito só para uma placa de avaliação no Google, uma vez por cliente, com documento e e-mail. Sai `TipoDeContrato.TermoDoCliente` e o modelo `termo-do-cliente.html`. `Plataforma:CnpjDaRevendaDaAvalia` passa a 39914870000101. |
| 26/10/2026 | Dependências - pacotes .NET na 10.0.14 (EF Core, ASP.NET Core, Hosting), Sentry 6.12.1, Swashbuckle 10.3.1, QRCoder 1.8.1; no front, Sentry 11.9.0, react-hook-form 7.91.0, zustand 5.0.16, barcode-detector 3.2.3, zxing-wasm 3.1.6. Fora desta rodada, por serem versão maior: TypeScript 7, `@hookform/resolvers` 6 e `@types/node` 26. Registro de mudanças de 21 a 24/10 movido para `docs/historico/`. |
| 26/10/2026 | Página pública `/privacidade` (`web/src/legal`), com o texto em `politica-de-privacidade.md` lido por `?raw` e desenhado por `Markdown.tsx` sobre o `Table` do design system. Modelo `Impressao/Modelos/termo-do-cliente.html` embutido no assembly, sem uso pela venda ainda. `CategoriaDeDespesaDaOperadora.TarifaDoGateway = 7`. |
| 26/10/2026 | `Vendas.Resumo` e `PainelDoVendedor.Resumo` tratam como paga a venda `DoSistemaAnterior` com uma linha `Receita` não estornada cuja origem é a própria venda (`PagaNoAnterior`). `HistoricoNoPainelTest`. |
| 26/10/2026 | `/now` - abaixo de 1024 px, `.abertura-texto` vira `display: contents` e os blocos da abertura se ordenam por `order` em volta da figura, sem mexer no HTML. |
| 26/10/2026 | Busca sem acento - `Application/Busca.cs` (`Busca.Normalizar`) é o ponto único das buscas por texto das listagens, mapeada com `HasDbFunction` para `avalia_normalizar(text)`, que a migration `BuscaSemAcento` cria sobre `CREATE EXTENSION unaccent` (trusted no PG 13+, criada pelo dono do banco; função `IMMUTABLE` com o dicionário explícito, execução concedida a `avalia_app`). Os dois lados da comparação passam por ela e o `Contains` continua `strpos`, então `%` e `_` seguem literais; a busca continua cortada em 100 caracteres (a da auditoria agora também). Sem índice trigram: não medido, as listas já filtram por revenda antes. `BuscaSemAcentoTest`. ADR 0023. |
| 26/10/2026 | E-mails sensíveis cifrados em repouso - `ICifraDeEmails`/`CifraDeEmails` (AES-256-GCM, nonce aleatório por linha, formato `avalia-cifra:v1:` + base64, chave do e-mail como dado associado) com `Seguranca__ChaveDosEmails` (32 bytes em base64, igual na API e no Worker) e `Seguranca__ChaveAnteriorDosEmails` para a rotação. `FilaDeEmails` cifra ao enfileirar, `EnvioDeEmails` abre antes do envio; linha antiga em claro ainda envia; chave que não abre vira falha definitiva (`EmailNaFila.FalharDefinitivamente`) com o conteúdo apagado e sem o texto no log. A subida falha fora do Development sem a chave (`ValidadorDasOpcoesDeSeguranca`); Development e testes usam chave embutida. Sem migration (usa as colunas `assunto` e `texto`). `EmailCifradoTest`. ADR 0024. |
| 26/10/2026 | Financeiro da operadora (ADR 0025) - migration `FinanceiroDaOperadora` (tabelas `lancamentos_da_operadora`, `despesas_da_operadora`, `fechamentos_da_operadora`, `custos_de_lote`, colunas `revendedores.do_grupo_da_operadora`, `grupo_definido_em` e `grupo_definido_por`, políticas de RLS, gatilho do mês fechado e backfill). Rotas `/operadora/financeiro/*` e `PUT /operadora/revendedores/{id}/grupo`; casos de uso `LivroDaOperadora`, `DespesasDaOperadora`, `FechamentosDaOperadora`, `CustosDeLote`, `ResultadoDoGrupo`; `IEscopoDeRevenda`; `Dre` passa a receber o `TimeProvider`. Tela `web/src/operadora/TelaDoResultadoDoGrupo`. `ResultadoDoGrupoTest`, `LivroDaOperadoraNoBancoTest`, `LivroDaOperadoraDominioTest`. |
| 26/10/2026 | `POST /placas/{codigo}/renovacao` e `/renovacao/recebimento` passam do grupo `equipe` para o `dono`; `Renovacoes.AbrirRenovacao` recusa o papel Vendedor; `RecebimentosPorFora.DoDia` conta só vendas. Front - o cartão Renovação da ficha só aparece para o revendedor, e a carteira do vendedor só oferece o WhatsApp. |
| 26/10/2026 | Operação - comandos de manutenção com `docker compose run` levam `-T` e `timeout`; o `run` com terminal anexado pode girar por dias na CPU se o terminal cai (24/10, causa do limite de 40% da CPU aplicado pela Hostinger). |
| 26/10/2026 | VPS de um núcleo - o Compose de produção dá prioridade de CPU por serviço (`cpu_shares`: banco, API e Nginx 2048; Worker e migrar 512; Gotenberg 256; certbot e backup 128), que só pesa quando há disputa, e o Deploy compila uma imagem por vez (`COMPOSE_PARALLEL_LIMIT=1`). |
| 25/10/2026 | Cópia fora do VPS - `backup.sh` envia a cópia, criptografada com `BACKUP_SENHA`, a um bucket compatível com S3 (`S3_ENDPOINT`, `S3_BUCKET`, `S3_KEY_ID`, `S3_APP_KEY`; assinatura SigV4 pelo `curl`, ETag conferido com o MD5), apaga do bucket o que passou de `BACKUP_DIAS_NO_BUCKET` dias só depois de um envio conferido, e tem o modo `testar-bucket`. Falha do bucket não segura o Deploy. Testado contra o Cloudflare R2. |
| 25/10/2026 | O Deploy repassa `TURNSTILE_CHAVE_SITE` (variável) e `TURNSTILE_CHAVE_SECRETA` (segredo) do ambiente `producao` ao Compose, sem passar pelo `.env` do servidor. |
| 25/10/2026 | Paginação - toda rota GET de lista que cresce responde `{ itens, total, proxima }` (`Application/Pagina.cs`) com `pagina` e `tamanho` (padrão 50, máximo 100 aplicado no servidor, valor fora da faixa é corrigido), ordem total com `Id` no fim e `busca` aplicada antes da página (cortada em 100 caracteres); mensagens de chamado usam o cursor `antes`; resumos próprios em `/vendas/resumo`, `/operadora/chamados/resumo` e `?situacao=` em `/servicos`. `/placas` conta o estado no SQL; `PaginaDePlacas.placas` e `PaginaDeAuditoria.registros` viraram `itens`. A busca por acento ficou equivalente à antiga em 26/10/2026 (ver ADR 0025). Front - `useListaPaginada`, `ListaPaginada` e `Paginacao` (`lib/paginacao.ts`, `componentes`), busca com atraso de 300 ms na API, chave de consulta por página, busca e filtro. Migration `IndicesDaPaginacao` (sete índices em `vendas`, `pedidos_placas`, `links`, `lancamentos`, `repasses` e `envios_ao_erp`). `PaginacaoTest`. |
| 25/10/2026 | Cadastro pendente (ADR 0022) - `FinalidadeDoLink.CadastroPendente` marca o cadastro público não confirmado, sem coluna nova; `POST /cadastro` sem senha devolve 202 e `POST /cadastro/reenvio` devolve 204; `Cadastro` (Application) orquestra limites e situação, com `IContas` ampliado e `JanelaDeTentativas`; `IVerificadorDeCaptcha` em `VerificadorTurnstile` (`HttpClient` tipado, falha fechada, desligado sem `Turnstile__ChaveSecreta`); o Worker roda `LimpezaDeCadastros` a cada 10 minutos, separada da `LimpezaDeExpirados`; opções `Cadastro__*`, `Limites__CadastrosPorHoraPorIp`; a CSP do Nginx libera `challenges.cloudflare.com`; o front lê `VITE_TURNSTILE_CHAVE_SITE` no build. `CadastroPublicoTest`, `CadastroComCaptchaTest`, `VerificadorTurnstileTest`. |
| 25/10/2026 | `Idempotency-Key` nas gravações POST autenticadas, num middleware único (`IdempotenciaDaRequisicao`): chave por usuário e rota, resposta gravada por 24 h em `chaves_de_idempotencia` (RLS próprio; a operadora grava com revendedor nulo), 422 com corpo diferente, 409 na execução simultânea, hash HMAC do corpo; rotas de segredo ficam de fora (`SemIdempotencia`); o front manda uma chave por chamada POST. Migration `IdempotenciaEEmailsSensiveis`. |
| 25/10/2026 | Refresh token com tolerância de 10 s (`GestorDeSessoes.JanelaDeTolerancia`, até 10 reenvios por token); reuso fora da janela, ou depois de sair, revoga a família. `emails.sensivel`: assunto e texto com link de senha ou código são apagados depois do envio e na falha definitiva, e o parado além de 2 dias é descartado (a cifra em repouso veio em 26/10/2026). O Worker apaga a cada 10 min chaves de idempotência vencidas, famílias de sessão vencidas, links de senha vencidos há 7 dias e e-mails com mais de 30 dias (`LimpezaDeExpirados`). |
| 25/10/2026 | O anexo do backup por e-mail sai criptografado (`openssl` AES-256-CBC com PBKDF2, `BACKUP_SENHA`); sem a senha o e-mail é desligado e a cópia no disco continua, que não é criptografada. Datas com fuso diferente de zero em corpo JSON são aceitas e normalizadas para UTC (`DataEmUtc`); o contrato OpenAPI não muda. |
| 25/10/2026 | `Clientes/PrivacidadeDoCliente` é o único ponto da regra de contato do cliente por papel (cadastrante = vendedor da primeira venda do cliente com `CriadoEm >= VendidaEm`); passam por ele `Vendas.Detalhe`, `FichaDaPlaca` e `PainelDoVendedor`. `Links` filtra o vendedor pelos clientes das vendas dele e pelo registro `LinkCriado`. `Financeiro/RecebimentosPorFora` (limites, pendências e conferência, ADR 0021): rotas `GET/POST /comissoes/conferencia` e `PUT /equipe/{id}/limites-por-fora`; `Comissoes.Repassar` deixa de fora as vendas não conferidas e `SaldoDeComissao.AguardandoConferenciaCents`. Mapa - lógica em `painel/mapaDeLugares.ts`, testada com o Google simulado, com a posição de cada `lugarNoGoogle` em cache até desmontar. `DadoDeClienteAlheioTest`, `RecebimentoPorForaDoVendedorTest`, `LinksDoVendedorTest`, `mapaDeLugares.test.ts`. |
| 25/10/2026 | Revisão do front - `api/consultas.ts` esvaziado ao sair, ao ter a sessão recusada e ao entrar como outra pessoa; `chamarApi` une gravações idênticas em andamento (a gravação ainda não tem chave de idempotência no servidor); 401 com renovação indisponível não encerra a sessão; SignalR religa a cada 10 s e refaz as consultas ao reconectar; invalidação de `financeiro`, `placa`, `clientes` e `lotes` nas mutações e nos avisos; datas de negócio em Brasília (`lib/data.ts`); Sentry sem o fragmento da URL (`lib/sentry.ts`), onde vão os tokens de convite e senha; leitor de QR e gravação de NFC liberados ao sair; cópia recusada avisa (`lib/area.ts`). `cacheDaSessao.test.ts`, `invalidacao.test.tsx`, `envio-duplo.spec.ts`. |
| 25/10/2026 | Revisão de acesso e entrada - corpo ausente ou quebrado responde 400 (`PedidoMalFormadoEm400`) e violação de unicidade responde 422 (`DuplicidadeEm422`), ambos fora do Sentry; `Destino.Conferir` recusa caractere de controle e grava só ASCII (`Destino.ParaCabecalho` também na leitura de /q e /l); a rota pública ignora o token na leitura (`AcessoDaRequisicao`); `IBancoDeDados.TravarLinksDaRevenda` na criação e alteração de links; migration `AuditoriaDaOperadoraSoDeInsercao` (REVOKE UPDATE, DELETE); entrada gasta o mesmo tempo para e-mail sem conta; `backup.sh` com `umask 077`. `RevisaoDeAcessoTest`, `PermissoesDoBancoTest`, `VarreduraDeRotasTest`. |
| 25/10/2026 | Concorrência e estorno - `IBancoDeDados.SegurarOrigemDaCobranca` (`pg_advisory_lock` de sessão, sem transação durante a chamada ao Pagar.me) em `CriarLink`, `Vendas.ReceberPorFora`, `Renovacoes.ReceberPorFora` e `Vendas.Cancelar`. `IGatewayDePagamento.EstornarParte` recebe o devolvido registrado e não repete a devolução se o `canceled_amount` do Pagar.me já a cobre (a confirmar no sandbox); `Cobranca.ConferirEstorno` roda antes de chamar o gateway. Charge `canceled` com `paid_at` é lida como estornada. `ProcessadorDeEventos` conta tentativa para qualquer falha, e `Retomadas.Isolar` descarta o contexto e manda a cobrança que não aplica para o fim da fila (Pagar.me fora do ar continua parando a rodada). `RevisaoDeDinheiroTest`, `ContratoDeEstornoTest`. |
| 25/10/2026 | `FichaDaPlaca.Ler` restringe o vendedor à venda dele. `AutorizacaoPorRecursoTest` cobre a ficha entre vendedores, a lista fechada de rotas sem login, a política mínima por prefixo e o RLS no papel da aplicação em toda tabela com `revendedor_id`. |
| 25/10/2026 | `DELETE /placas/{codigo}/renovacao/recebimento` (`Renovacoes.DesfazerRecebimento`, só o dono) - `Renovacao.Desfazer` e `Placa.DesfazerRenovacao` devolvem o vencimento de antes e reabrem a renovação; `Cobrancas.DesfazerPorFora` lança o estorno. Ações de auditoria `RenovacaoRecebidaPorFora` e `RenovacaoDesfeita`. `FichaDto.UltimaRenovacao`. Ficha da placa com diálogo de confirmação antes do recebimento por fora. |
| 25/10/2026 | Comando `--importar-historico` (`Importacao.ImportarHistorico`) e saída `--historico` do `converter_do_sales.py` - lançamentos `Receita`, `ComissaoDevida` e `ComissaoPaga` com origem e venda iguais à venda importada, na competência de `pago_em` e de `comissao_paga_em` em Brasília; tudo ou nada com `--ensaio`; roda de novo sem duplicar (receita já lançada é pulada, índice único de `lancamentos`); recusa mês fechado e confere a competência gravada contra o gatilho `lancamento_em_mes_aberto`; `Comissoes.Repassar` ignora a venda com `ComissaoPaga` própria. `HistoricoTest`. |
| 25/10/2026 | Front do caixa - `financeiro/CartaoDoCaixa.tsx` (`useCaixa`, `useSalvarSaldoInicial`, `useExcluirSaldoInicial`), `mesSomado` em `fatias.ts`; tabela do Mapa com `tipo` e `AcoesDaLinha`. |
| 25/10/2026 | Tabela - `TableHead` com tipo da coluna (`texto`, `numero`, `data`, `situacao`, `acoes`), copiado para as células e aplicado pelo `index.css`; rolagem própria e cabeçalho fixo só a partir de `md`; `AcoesDaLinha` aceita `para` e `href`. |
| 25/10/2026 | Acessibilidade - área de rolagem sem `tabIndex` fixo (Sonar S6845); `useRegiaoRolavel` dá `tabindex`, `role="region"` e nome só quando a área rola e não tem nada focável (axe `scrollable-region-focusable`). |
| 25/10/2026 | Caixa - `Application/Financeiro/Caixa.cs`, rotas `GET/PUT/DELETE /financeiro/caixa`; tipos de lançamento `AporteDeSocio` e `SaldoInicialDoCaixa` (texto, sem migration), natureza `Aporte` na retirada, ações de auditoria `SaldoInicialDoCaixaDefinido` e `SaldoInicialDoCaixaExcluido`. O caixa soma as linhas de `Receita` e `AporteDeSocio` e tira `TaxaAvalia`, `ComissaoPaga`, `Despesa` e `RetiradaDeSocio` da competência do saldo inicial em diante, e os pedidos de placas pagos desde o início dela. `Dre.Carregar` reconhece em um mês a receita sem cobrança de venda `DoSistemaAnterior`, com o custo. `CaixaTest`. |
| 25/10/2026 | `Rodape` da entrada com `<img>` dos SVG oficiais em `web/public/parceiros` (`pagarme.svg` verde, `stone.svg`); `resend.svg` removido. |
| 25/10/2026 | Mapa - `enquadramento` corta 10% de cada ponta em latitude e longitude a partir de 8 lugares, `fitBounds` com 24 px e zoom máximo 16; marcadores guardados e retirados na troca. Deploy - o passo "Limpar imagens velhas" apaga as imagens `avalia-sales-*` com tag de commit, menos a versão no ar e a anterior, e o cache de build com mais de 7 dias. SonarCloud pelo CI (`SONAR_TOKEN`, `SONAR_ORGANIZACAO`, `SONAR_PROJETO` gravados; job `qualidade` com `continue-on-error`). CI sem script de instalação de dependência (`npm ci --ignore-scripts`) e sem `npx`: Playwright e Vite pelo `npm run`, na versão do `package-lock`; gitleaks-action fixada por SHA (v2.3.9); expressões regulares de e-mail sem retrocesso no front e no conversor do Sales; id da linha da Nova venda por `crypto.getRandomValues` fora de HTTPS; rodapé do PDF com `lang` e `title`, sem mudança no documento gerado. |
| 25/10/2026 | Documento revisto - texto reorganizado, estado das integrações e das fases atualizado. |
| 25/10/2026 | Revisão de arquitetura, segundo lote - Retomadas reconsultam a cobrança paga uma vez por dia (chargeback e estorno pelo painel do Pagar.me sem webhook, `ChargebackTest`); a renovação da sessão passa por `navigator.locks` e duas abas não gastam o mesmo refresh token; Nginx com `/q/` e `/l/` respondendo 503 com página de espera e recarga em 3 s enquanto a API reinicia, e log de acesso sem query string (`sem_query`); `backup.sh` só aceita a cópia que o `pg_restore --list` lê com os dados de `lancamentos`; Worker manda check-in ao Sentry a cada 5 minutos (monitor `avalia-sales-worker`, alerta com 2 faltas); pedido abortado por quem chamou responde 499 sem virar erro; monitor de disponibilidade do Sentry em `/api/health` a cada 60 s. |
