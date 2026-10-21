# Avalia Sales

Plataforma de revenda de placas com QR Code e NFC, para revendedores que vendem
com a própria equipe. API e Worker em .NET 10, PostgreSQL com isolamento por revendedor no EF Core
e no RLS do banco, cobrança no Pagar.me e assinatura na D4Sign.

## Documentação

- [docs/PDD.md](docs/PDD.md) - produto: papéis, jornada, regras de negócio, fases.
- [docs/SDD.md](docs/SDD.md) - software: arquitetura, isolamento, dados, integrações, infra.
- [docs/adr](docs/adr/README.md) - registros de decisão, no formato MADR.

## Rodar os testes

Os testes de integração usam um Postgres de verdade: cada execução cria um
banco descartável e apaga no fim.

```bash
dotnet test
```

Sem configuração, o teste conecta em `localhost` com o usuário do sistema. Para
outro servidor, defina a conexão de administrador:

```bash
export AVALIA_SALES_PG_ADMIN="Host=localhost;Username=postgres;Password=...;Database=postgres"
```

## Rodar sem Docker

Com o Postgres local no ar e um banco `avalia_sales_dev` criado:

```bash
export ASPNETCORE_ENVIRONMENT=Development DOTNET_ENVIRONMENT=Development
export ConnectionStrings__Banco="Host=localhost;Database=avalia_sales_dev;Username=avalia_app;Password=<senha do papel>"
export Jwt__Chave="$(openssl rand -base64 48)"
# Pagar.me e D4Sign: chaves de sandbox, ou valores falsos para só navegar.
# Em Development o Pagar.me roda simulado (appsettings.Development.json): o Pix fica pago 20 s depois.
export Pagarme__ChaveSecreta=sk_test_... Pagarme__RecebedorAvalia=rp_... Pagarme__SegredoDoWebhook=<32+ caracteres>
export D4Sign__TokenApi=... D4Sign__SegredoHmac=... D4Sign__Cofre=... D4Sign__UrlDoWebhook=https://...
# Opcionais: sem eles o e-mail espera na fila e a busca no Google pede o link colado
export Resend__ChaveApi=re_... Resend__Remetente="Avalia Sales <avisos@seu-dominio>" Google__ChaveDoServidor=...

ConnectionStrings__Dono="Host=localhost;Database=avalia_sales_dev;Username=$USER" \
  dotnet run --project src/AvaliaSales.Api --no-launch-profile -- --migrar
dotnet run --project src/AvaliaSales.Api --no-launch-profile -- --demonstracao
ASPNETCORE_URLS=http://127.0.0.1:5199 dotnet run --project src/AvaliaSales.Api --no-launch-profile
dotnet run --project src/AvaliaSales.Worker --no-launch-profile   # em outro terminal
```

Front, em outro terminal:

```bash
cd web && npm ci && npm run dev   # http://localhost:5173, /api vai para a API local
```

Páginas - `http://127.0.0.1:5199/swagger` (Swagger UI com todas as rotas e o
botão Authorize para o token), `/health` e a leitura de placa em `/q/{codigo}`.

A demonstração cria, com a senha `demonstracao-123` - a operadora
`operadora@avalia.dev`; a revenda A com `dono@revenda-a.dev`, `bruna@revenda-a.dev`
e `carlos@revenda-a.dev`, placas ativas, vendas pagas e pendentes, leituras e um
lote (LT-0002) esperando ativação; a revenda B com `dono@revenda-b.dev` e um
pedido aguardando pagamento. Preços, contratos, recebedores e pagamentos são
simulados.

Revendedor e operadora pela linha de comando (o revendedor também se cadastra
pela tela):

```bash
dotnet run --project src/AvaliaSales.Api --no-launch-profile -- \
  --novo-revendedor --nome="Avalia" --email=dono@exemplo.com.br --senha=<10 caracteres ou mais>
dotnet run --project src/AvaliaSales.Api --no-launch-profile -- \
  --nova-operadora --nome="Operação Avalia" --email=operadora@exemplo.com.br --senha=<10 caracteres ou mais>
```

## Subir com Docker Compose

```bash
cp .env.example .env   # preencha banco, JWT, Pagar.me e D4Sign
docker compose up --build
```

A API fica em `http://127.0.0.1:8080`, com `GET /health` para conferir o banco.
O Worker sobe junto e processa os webhooks recebidos.

## Camadas

| Projeto | Papel |
| --- | --- |
| `AvaliaSales.Domain` | Entidades e regras, sem dependência externa |
| `AvaliaSales.Application` | Casos de uso e as interfaces de que eles precisam |
| `AvaliaSales.Infrastructure` | EF Core, Postgres, Identity, migrations |
| `AvaliaSales.Api` | Rotas HTTP, autenticação JWT, webhooks, saúde |
| `AvaliaSales.Worker` | Caixa de entrada dos webhooks, retomadas, conferência de cobrança e fila de e-mails |
| `web` | Front em React 19: entrada e catálogo do revendedor |
