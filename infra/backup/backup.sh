#!/bin/sh
# Cópia diária do banco com pg_dump (formato custom, restaura com pg_restore),
# guardada por BACKUP_DIAS dias. Roda no contêiner "backup" do compose de produção;
# para uma cópia na hora: docker compose ... exec backup sh /backup.sh agora
#
# Com BACKUP_PARA e a chave do Resend, a cópia também sai por e-mail: o disco do
# VPS protege de engano humano, a caixa de e-mail protege da perda do VPS. O
# anexo tem dado de cliente e hash de senha; a caixa de destino é da Avalia e não
# tem encaminhamento automático. Acima do limite do Resend (40 MB depois do
# base64), o e-mail sai sem anexo, avisando que a cópia ficou só no disco.
set -eu

LIMITE_DO_ANEXO=28000000

enviar() {
  arquivo="$1"
  nome=$(basename "$arquivo")
  tamanho=$(wc -c < "$arquivo")
  dia=$(TZ=America/Sao_Paulo date +%d/%m/%Y)
  corpo=/tmp/email.json
  if [ "$tamanho" -le "$LIMITE_DO_ANEXO" ]; then
    {
      printf '{"from":"%s","to":["%s"],"subject":"Cópia do banco do Avalia Sales - %s",' "$RESEND_REMETENTE" "$BACKUP_PARA" "$dia"
      printf '"text":"Cópia diária do banco, %s bytes. Restauração em docs/OPERACAO.md.",' "$tamanho"
      printf '"attachments":[{"filename":"%s","content":"' "$nome"
      base64 -w0 "$arquivo"
      printf '"}]}'
    } > "$corpo"
  else
    printf '{"from":"%s","to":["%s"],"subject":"Cópia do banco do Avalia Sales - %s - SEM ANEXO","text":"A cópia tem %s bytes e passou do limite do e-mail. Ela ficou só no disco do VPS (%s). Configure outro destino externo."}' \
      "$RESEND_REMETENTE" "$BACKUP_PARA" "$dia" "$tamanho" "$nome" > "$corpo"
  fi
  if curl -fsS --retry 3 --retry-all-errors -o /dev/null https://api.resend.com/emails \
      -H "Authorization: Bearer $RESEND_CHAVE_API" -H "Content-Type: application/json" \
      -H "Idempotency-Key: backup-$nome" --data-binary @"$corpo"; then
    echo "cópia enviada para $BACKUP_PARA"
  else
    echo "envio da cópia falhou" >&2
  fi
  rm -f "$corpo"
}

# Às 02:00 de Brasília (05:00 UTC), fora do horário de uso. Esperar a hora, e não
# 24 h a partir da subida, evita uma cópia (e um e-mail) a cada publicação.
esperar() {
  agora=$(date -u +%s)
  alvo=$(date -u -d "05:00" +%s)
  [ "$alvo" -gt "$agora" ] || alvo=$(date -u -d "tomorrow 05:00" +%s)
  sleep $((alvo - agora))
}

copiar() {
  arquivo="/backups/avalia_sales-$(date -u +%Y%m%dT%H%M%SZ).dump"
  if pg_dump -Fc -f "$arquivo.parcial"; then
    mv "$arquivo.parcial" "$arquivo"
    echo "backup gravado: $arquivo"
    if [ -n "${BACKUP_PARA:-}" ] && [ -n "${RESEND_CHAVE_API:-}" ] && [ -n "${RESEND_REMETENTE:-}" ]; then
      enviar "$arquivo"
    fi
  else
    rm -f "$arquivo.parcial"
    echo "backup falhou" >&2
    return 1
  fi
  find /backups -name 'avalia_sales-*.dump' -mtime +"${BACKUP_DIAS:-14}" -delete
}

# "agora" é a cópia antes de cada publicação: falhar aqui segura o deploy.
if [ "${1:-}" = agora ]; then
  copiar
  exit $?
fi

while true; do
  esperar
  copiar || true
done
