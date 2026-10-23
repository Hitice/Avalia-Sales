# Avalia Sales

Plataforma de revenda das placas de avaliação da Avalia, com QR Code e NFC. A
Avalia opera a plataforma e vende as placas em pacotes; cada revenda vende com
a própria equipe, cobra o cliente final pelo Pagar.me, assina contrato e
distrato pela D4Sign e acompanha vendas, comissões e clientes num painel com a
própria marca.

Em produção em [sales.avaliaone.com.br](https://sales.avaliaone.com.br). A
versão publicada aparece no rodapé do app e segue o `version` de
`web/package.json`, com uma tag `vX.Y.Z` por entrega.

## Documentação

| Documento | Conteúdo |
| --- | --- |
| [docs/PDD.md](docs/PDD.md) | Produto - papéis, jornada, telas, regras de negócio, dinheiro e decisões pendentes |
| [docs/SDD.md](docs/SDD.md) | Software - arquitetura, isolamento entre revendas, modelo de dados, integrações, segurança, infraestrutura e CI |
| [docs/adr](docs/adr/README.md) | Cada decisão estrutural, no formato MADR |
| [docs/OPERACAO.md](docs/OPERACAO.md) | Instalação no VPS, publicação, volta, backup, restauração e importação de placas |

Toda mudança de regra, tabela, integração ou configuração atualiza o PDD ou o
SDD no mesmo commit, com uma linha no registro de mudanças do fim.

## Arquitetura

```
Navegador (React) ──┐
Leitura do QR ──────┼── Nginx ── API ──┬── PostgreSQL 17 (RLS por revenda)
Webhooks ───────────┘              │   ├── Pagar.me, D4Sign, Resend, Google Places, BrasilAPI
                         Worker ───┘   └── Gotenberg (PDF do contrato e do distrato)
```

| Projeto | Papel |
| --- | --- |
| `src/AvaliaSales.Domain` | Entidades e regras de negócio, sem dependência de outro projeto |
| `src/AvaliaSales.Application` | Casos de uso e as interfaces de que eles precisam |
| `src/AvaliaSales.Infrastructure` | EF Core e PostgreSQL, Identity, migrations, clientes das integrações, modelos de documento |
| `src/AvaliaSales.Api` | Rotas HTTP por área, autenticação JWT, webhooks, leitura do QR e modos de linha de comando |
| `src/AvaliaSales.Worker` | Processa os webhooks, reconsulta cobranças e assinaturas, gera os documentos e envia e-mails e pedidos ao ERP |
| `web` | Front único em React 19: painel da revenda, área da operadora, app do vendedor (PWA com NFC e câmera) e página do QR |
| `tests/AvaliaSales.Tests` | Testes de unidade, de contrato HTTP e de fluxo completo no PostgreSQL |

A revenda é o tenant: toda tabela de negócio tem `revendedor_id`, com filtro
global do EF Core e RLS no banco. A API conecta com o papel `avalia_app`, sem
superusuário e sem `BYPASSRLS`; as migrations rodam como dono do banco.

Stack: .NET 10 LTS (ASP.NET Core, Worker Service, EF Core 10, Identity, SignalR),
PostgreSQL 17, React 19 com Vite, React Router, TanStack Query, Tailwind 4 e
componentes no modelo do shadcn/ui. Versões fixadas em
`Directory.Packages.props` e `web/package.json`.

## Desenvolvimento

Requisitos: .NET 10 SDK, Node 24 e PostgreSQL 17 (no macOS,
`brew install postgresql@17` e `pg_ctl -D /opt/homebrew/var/postgresql@17 start`).

### Testes

```bash
dotnet test                 # API: cria um banco descartável por execução e apaga no fim
cd web && npm run test      # front: Vitest
cd web && npm run e2e       # ponta a ponta: Playwright com axe, contra a API local
```

O `dotnet test` conecta em `localhost` com o usuário do sistema. Para outro
servidor, defina a conexão de administrador em `AVALIA_SALES_PG_ADMIN`.

O `ContratoDaApiTest` regenera `web/openapi.json` quando uma rota muda e falha
nessa primeira execução; depois, `cd web && npm run tipos` atualiza
`src/api/esquema.d.ts`. O CI recusa contrato ou tipos desatualizados.

### API e Worker locais

Com um banco `avalia_sales_dev` e o papel `avalia_app` criados:

```bash
export ASPNETCORE_ENVIRONMENT=Development DOTNET_ENVIRONMENT=Development
export ConnectionStrings__Banco="Host=localhost;Database=avalia_sales_dev;Username=avalia_app;Password=<senha do papel>"
export Jwt__Chave="$(openssl rand -base64 48)"
export Pagarme__ChaveSecreta=sk_test_local Pagarme__RecebedorAvalia=rp_local Pagarme__SegredoDoWebhook=<32 caracteres ou mais>
export D4Sign__TokenApi=local D4Sign__SegredoHmac=<segredo> D4Sign__Cofre=local D4Sign__UrlDoWebhook=https://exemplo/webhooks/d4sign

ConnectionStrings__Dono="Host=localhost;Database=avalia_sales_dev;Username=$USER" \
  dotnet run --project src/AvaliaSales.Api --no-launch-profile -- --migrar
dotnet run --project src/AvaliaSales.Api --no-launch-profile -- --demonstracao
ASPNETCORE_URLS=http://127.0.0.1:5199 dotnet run --project src/AvaliaSales.Api --no-launch-profile
dotnet run --project src/AvaliaSales.Worker --no-launch-profile   # em outro terminal, com as mesmas variáveis
```

Em Development, o Pagar.me, o Protheus e o conversor de PDF rodam simulados
(`appsettings.Development.json`): o Pix fica pago 20 segundos depois, e o
contrato sai como um PDF mínimo. Resend, Google Places e BrasilAPI são opcionais:
sem chave, o e-mail espera na fila e a busca no Google pede o link colado.

Swagger em `http://127.0.0.1:5199/swagger` (só em Development), saúde em
`/health` e leitura de placa em `/q/{codigo}`.

### Front

```bash
cd web && npm ci && npm run dev   # http://localhost:5173; /api e /q/ vão para a API local
npm run storybook                 # catálogo de componentes, porta 6006
```

### Dados de demonstração

O `--demonstracao` cria, todos com a senha `demo-123`:

| Login | Papel |
| --- | --- |
| `operador@avalia.dev` | Operadora |
| `cliente@a.dev` e `vendedor@a.dev` | Dono e vendedora da revenda A, com placas no ar, vendas pagas e pendentes e um lote esperando ativação |
| `cliente@b.dev` e `vendedor@b.dev` | Dono e vendedor da revenda B |

Operadora e revenda também podem ser criadas pela linha de comando (a revenda
também se cadastra pela tela):

```bash
dotnet run --project src/AvaliaSales.Api --no-launch-profile -- \
  --nova-operadora --nome="Operação Avalia" --email=operacao@exemplo.com.br --senha=<10 caracteres ou mais>
dotnet run --project src/AvaliaSales.Api --no-launch-profile -- \
  --novo-revendedor --nome="Revenda" --email=dono@exemplo.com.br --senha=<10 caracteres ou mais>
```

### NFC e câmera no celular

Web NFC e câmera só funcionam em HTTPS ou em `localhost`. Para testar no
celular na mesma rede, gere um certificado local e suba o front no IP da rede:

```bash
IP=$(ipconfig getifaddr en0)   # no Linux: hostname -I | cut -d' ' -f1
openssl req -x509 -newkey rsa:2048 -sha256 -days 30 -nodes -keyout chave.pem -out certificado.pem \
  -subj "/CN=Avalia Sales local" -addext "subjectAltName=IP:$IP,DNS:localhost"
cd web && FRONT_NA_REDE=1 FRONT_CERTIFICADO=../certificado.pem FRONT_CHAVE=../chave.pem npm run dev
```

Suba a API com `Plataforma__UrlPublica` e `Plataforma__UrlDoPainel` em
`https://$IP:5173`, para o QR e a tag gravada apontarem para um endereço que o
celular alcança. A gravação de NFC só existe no Chrome do Android; no iPhone, o
app abre o NFC Tools com o endereço copiado.

## Produção

VPS Linux com Docker Compose (`docker-compose.producao.yml`): Nginx com
Let's Encrypt, API, Worker, Gotenberg, PostgreSQL e backup diário. Só o Nginx
publica porta.

Publicação: push no `main`, CI verde (build, testes da API e do front, ponta a
ponta com axe, segredos e qualidade) e o workflow Deploy, que espera a aprovação
do ambiente `producao` no GitHub e roda no runner do próprio VPS. Voltar é
publicar o commit anterior. Passo a passo, segredos e backup em
[docs/OPERACAO.md](docs/OPERACAO.md).
