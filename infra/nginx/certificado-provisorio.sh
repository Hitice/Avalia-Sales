#!/bin/sh
# Sem certificado ainda (primeira subida), o Nginx não sobe a porta 443. Gera um
# provisório autoassinado para ele subir e responder o desafio do Let's Encrypt
# na porta 80; o certbot grava o de verdade por cima e o Nginx recarrega.
set -eu
pasta="/etc/letsencrypt/live/${DOMINIO}"
if [ ! -f "$pasta/fullchain.pem" ]; then
  apk add --no-cache openssl >/dev/null
  mkdir -p "$pasta"
  openssl req -x509 -nodes -newkey rsa:2048 -days 2 -subj "/CN=${DOMINIO}" \
    -keyout "$pasta/privkey.pem" -out "$pasta/fullchain.pem" >/dev/null 2>&1
  echo "certificado provisório criado para ${DOMINIO}"
fi
