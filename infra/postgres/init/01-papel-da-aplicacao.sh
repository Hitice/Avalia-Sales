#!/bin/sh
# Roda uma vez, quando o volume do Postgres nasce. Cria o papel com que a API
# conecta: sem superusuário e sem BYPASSRLS, para o RLS valer para ela.
set -eu
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -v senha="$PG_SENHA_APLICACAO" <<'SQL'
CREATE ROLE avalia_app LOGIN NOSUPERUSER NOBYPASSRLS PASSWORD :'senha';
SQL
