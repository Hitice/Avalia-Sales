#!/usr/bin/env bash
# Sobe a API e o Worker para os testes de ponta a ponta do CI: banco novo,
# papel da aplicação, migração, dados da demonstração, a API na 5199 e o
# Worker, que confere o Pix simulado.
# Localmente os testes usam a API que já estiver de pé.
set -euo pipefail

ADMIN="${E2E_PG_ADMIN:-postgresql://postgres:postgres@localhost:5432/postgres}"
BANCO=avalia_sales_e2e
SENHA_APP=e2e-senha-da-aplicacao
RAIZ="$(cd "$(dirname "$0")/../.." && pwd)"

psql "$ADMIN" -v ON_ERROR_STOP=1 -q <<SQL
DROP DATABASE IF EXISTS $BANCO WITH (FORCE);
CREATE DATABASE $BANCO;
DO \$\$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'avalia_app') THEN
    CREATE ROLE avalia_app LOGIN NOSUPERUSER NOBYPASSRLS PASSWORD '$SENHA_APP';
  END IF;
END \$\$;
SQL

export ASPNETCORE_ENVIRONMENT=Development DOTNET_ENVIRONMENT=Development
export ConnectionStrings__Banco="Host=localhost;Database=$BANCO;Username=avalia_app;Password=$SENHA_APP"
export Jwt__Chave="$(openssl rand -base64 48)"
export Pagarme__ChaveSecreta=sk_test_e2e Pagarme__RecebedorAvalia=rp_e2e Pagarme__SegredoDoWebhook=segredo-do-webhook-com-32-caracteres-ou-mais
export D4Sign__TokenApi=e2e D4Sign__CryptKey=e2e D4Sign__SegredoHmac=segredo-hmac-do-e2e-com-folga D4Sign__Cofre=e2e D4Sign__UrlDoWebhook=https://e2e.exemplo.com.br/webhooks/d4sign
export Plataforma__UrlPublica=http://localhost:4173 Plataforma__UrlDoPainel=http://localhost:4173
# Cada teste entra de novo, do mesmo IP.
export Limites__EntradasPorMinuto=100000
export ASPNETCORE_URLS=http://127.0.0.1:5199

cd "$RAIZ"
dotnet build src/AvaliaSales.Api -c Release -v quiet
dotnet build src/AvaliaSales.Worker -c Release -v quiet
ConnectionStrings__Dono="Host=localhost;Username=postgres;Password=postgres;Database=$BANCO" \
  dotnet run --project src/AvaliaSales.Api -c Release --no-build --no-launch-profile -- --migrar
dotnet run --project src/AvaliaSales.Api -c Release --no-build --no-launch-profile -- --demonstracao
nohup dotnet run --project src/AvaliaSales.Api -c Release --no-build --no-launch-profile > /tmp/api-e2e.log 2>&1 &
nohup dotnet run --project src/AvaliaSales.Worker -c Release --no-build --no-launch-profile > /tmp/worker-e2e.log 2>&1 &

for _ in $(seq 1 60); do
  if curl -fs http://127.0.0.1:5199/health > /dev/null; then echo "API de pé"; exit 0; fi
  sleep 1
done
cat /tmp/api-e2e.log
exit 1
