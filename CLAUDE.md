# Avalia Sales: plataforma de revenda

Plataforma multi-revendedor de placas com QR Code e NFC. Regra de negócio e
escopo estão em `docs/PDD.md`; arquitetura em `docs/SDD.md`; operação em
`docs/OPERACAO.md`; cada decisão estrutural em `docs/adr` (MADR); design
system em `DESIGN.md`. `AGENTS.md` e `.agents/skills` são links simbólicos para
este arquivo e para `.claude/skills`: uma fonte só para Claude Code e Codex.

Ler o documento certo antes de decidir arquitetura, regra ou escopo. Se o
código e o documento discordarem, vale o código e o documento é corrigido;
se for regra de negócio, perguntar antes (seção Documentação).

## Antes de mexer, ler

- Entidade, tabela, rota, teste ou biblioteca - skill `padroes`.
- Webhook, retentativa, Worker ou `Idempotency-Key` - skill `idempotencia`.
- Tela, componente, cor ou texto de tela - skill `design-system` e `DESIGN.md`
  (seção Texto para rótulo e mensagem de erro).
- Pagar.me - skill `pagarme-checkout-order`.
- Rota, tabela ou integração nova a publicar - skill `seguranca`.
- Limpeza - skill `codigo-morto`.
- Regras por pasta em `.claude/rules` (front e migrations); o agente `revisor`
  (`.claude/agents`) confere o diff antes de subir.

## Stack

- .NET 10 LTS: ASP.NET Core Web API, Worker Service, EF Core 10, ASP.NET
  Identity + JWT, SignalR. Solução em Api, Application, Domain, Infrastructure e
  Worker; Domain não depende de nenhum outro projeto. A versão mora só em
  `Directory.Build.props` e `Directory.Packages.props` (ADR 0002).
- PostgreSQL 17, com `revendedor_id` em toda tabela de negócio, filtro global
  do EF Core e RLS. A API conecta com o papel `avalia_app`, sem superusuário e
  sem BYPASSRLS; quem migra é o dono do banco.
- Front único em React 19 + Vite: painel do revendedor e da Avalia, app do
  vendedor (PWA com Web NFC e câmera) e página do QR, como rotas do mesmo app.
  React Router, TanStack Query, Zustand, React Hook Form + Zod, Tailwind 4 e
  componentes no modelo do shadcn/ui. White label por variáveis CSS (skill
  `design-system`).
- Integrações: Pagar.me v5 (checkout, split e cupom; marketplace na conta da
  Avalia), D4Sign, Resend, Google Maps JavaScript + Places (New), BrasilAPI
  (CNPJ e CEP no cadastro), Gotenberg (PDF do contrato e do distrato), Protheus
  por REST, desligado sem URL.
- Infra: VPS Linux de um núcleo, Docker Compose, Nginx, Let's Encrypt; GitHub
  Actions (CI nos runners do GitHub, Deploy no runner próprio do VPS),
  SonarCloud, Sentry.

## Regras

- Revendedor é o tenant. A Avalia é operadora da plataforma e também uma
  revendedora comum: nenhuma regra do código pergunta se o revendedor é a
  Avalia.
- Um domínio só para todos os revendedores; o tenant vem do login.
- Dinheiro em centavos (bigint, colunas `_cents`), taxa em pontos base
  (`_bps`). Preço, comissão e taxa ficam gravados na venda.
- Razão só recebe inserção; correção é estorno.
- Webhook grava o evento cru com chave única antes de processar e reconsulta a
  API de origem (skill `idempotencia`).
- Gravação POST autenticada aceita `Idempotency-Key` pelo middleware
  `IdempotenciaDaRequisicao`; rota que devolve segredo ou escreve cookie leva
  `SemIdempotencia()`.
- Rota GET de lista que cresce com o uso nasce paginada (`PedidoDePagina`,
  `Pagina<T>`, máximo 100; skill `padroes`).
- Migration só aditiva: a versão anterior continua lendo o banco novo.
- Ação da operadora que mexe em dinheiro de revenda passa por
  `AcoesSensiveisDaOperadora.Exigir` (segundo fator ligado; ADR 0027).
- `avaliaone.com.br/q/{codigo}` responde para sempre. O Sales do Avalia não é
  tocado até a virada.
- Registro novo nasce com criar, editar e excluir na tela.

## Rodar e testar

- `dotnet test` roda a suíte inteira, num processo só, igual ao CI. Precisa de
  um Postgres: local em `localhost` com o usuário do sistema, ou outro pela
  variável `AVALIA_SALES_PG_ADMIN`. Cada execução cria e apaga o próprio banco.
- Postgres local do Homebrew, sem serviço fixo:
  `pg_ctl -D /opt/homebrew/var/postgresql@17 start` (e `stop`).
- Tabela nova com `revendedor_id` precisa de `IsolarPorRevendedor` na
  migration; o teste `Toda_tabela_com_revendedor_tem_rls_ligado_e_forcado`
  falha sem ele.
- Migration nova: `dotnet tool restore` uma vez, depois
  `dotnet ef migrations add <Nome> -p src/AvaliaSales.Infrastructure -s src/AvaliaSales.Infrastructure -o Banco/Migrations`.
- Front, em `web/`: a lista de antes de entregar está em `.claude/rules/web.md`,
  igual ao job `web` do CI. `npm run e2e` (Playwright com axe) usa a API local
  de pé na 5199 com a demonstração (README), ou a que `web/e2e/api.sh` sobe
  (`E2E_PG_ADMIN` para o Postgres do Homebrew); `npm run tipos` depois de mudar rota (o
  `ContratoDaApiTest` regenera `web/openapi.json`). Comandos e variáveis de
  ambiente no `README.md`.

## Avalia, o repositório vizinho

`~/Documents/Development/Avalia` é o sistema atual (Laravel 12). Para ler as
regras do Sales, abrir a sessão com:

```bash
cd ~/Documents/Development/Avalia-Sales
claude --add-dir ~/Documents/Development/Avalia
```

As regras de venda estão em `app/Actions/Etiquetas` e em
`.claude/skills/sales/SKILL.md` de lá. É só leitura: este projeto não escreve no
Avalia nem lê o banco de produção dele.

## Escrita

- Português técnico e direto, sem travessão e sem metáfora. Em documento,
  rótulo e explicação separados por " - ".
- Documento descreve o produto e as decisões técnicas, sem bastidor do
  desenvolvimento: nada de falha passada de outro sistema, de motivo pessoal
  do projeto nem de quem pediu o quê. A Avalia pode aparecer comercialmente.
- Commit em inglês, no formato Conventional Commits (`feat:`, `fix:`, `chore:`,
  `docs:`, `refactor:`, `test:`, `ci:`; escopo opcional), resumo no imperativo.
- Mensagem de commit escrita em arquivo e enviada com `git commit -F`, só com o
  autor configurado no repositório: sem linha `Co-Authored-By` e sem rodapé de
  ferramenta, nem em PR.

## Documentação (PDD e SDD)

- `docs/PDD.md` é o produto: papéis, jornada, telas, regras de negócio,
  dinheiro, fases e decisões pendentes.
- `docs/SDD.md` é o software: arquitetura, isolamento, modelo de dados,
  integrações, segurança, infra, CI e decisões técnicas.
- Toda mudança de regra, comportamento, tabela, integração, configuração ou
  decisão atualiza o documento certo no mesmo commit, com uma linha no
  registro de mudanças do fim dele. Refatoração sem efeito visível não precisa.
- O que está no código vale mais que o documento: se discordarem, corrigir o
  documento no mesmo trabalho, ou perguntar se for regra de negócio.
- O registro de mudanças guarda as entradas recentes; as de 21 a 24/10/2026 estão em
  `docs/historico/`. Linha nova entra no topo da tabela do próprio documento.
- Decisão estrutural nova (tecnologia, isolamento, integração, segurança)
  ganha um ADR em `docs/adr` e uma linha no índice dele.
- Três travas cobram isso: o hook de fim de turno
  (`.claude/hooks/documentacao.sh`, chamado por `.claude/settings.json` e por
  `.codex/hooks.json`), o job `documentacao` do CI no push ao `main` e em pull
  request, e esta seção. Os dois conferem a mesma lista de caminhos.
  Refatoração sem efeito visível passa com `[sem-doc]` na mensagem do commit.

## Fluxo de trabalho

- Trabalho direto no `main`, sem branch. Push só quando o dono mandar subir.
- Pedidos que chegam no meio da execução entram numa fila; no fim do turno, a
  fila com feito, pendente e não verificado.
- Correção que o dono repetiu duas vezes vira linha aqui ou em `.claude/rules`;
  na terceira, vira teste ou hook.
