#!/bin/sh
# Cópia diária do banco com pg_dump (formato custom, restaura com pg_restore),
# guardada por BACKUP_DIAS dias. Roda no contêiner "backup" do compose de produção;
# para uma cópia na hora: docker compose ... exec backup sh /backup.sh agora
#
# Com BACKUP_PARA e a chave do Resend, a cópia também sai por e-mail: o disco do
# VPS protege de engano humano, a caixa de e-mail protege da perda do VPS. O
# anexo tem dado de cliente e hash de senha, por isso sai SEMPRE criptografado
# (AES-256-CBC, PBKDF2, senha em BACKUP_SENHA, obrigatória com BACKUP_PARA): sem
# a senha o script para, e nunca anexa a cópia em claro. Acima do limite do
# Resend (40 MB depois do base64), o e-mail sai sem anexo, avisando que a cópia
# ficou só no disco. O arquivo em disco continua em claro, sob o controle do VPS.
#
# Com S3_ENDPOINT, S3_BUCKET, S3_KEY_ID e S3_APP_KEY (qualquer armazenamento compatível com S3:
# Cloudflare R2, Backblaze B2...), a cópia também vai a um bucket fora do VPS, sempre criptografada
# com BACKUP_SENHA (sem a senha, nada vai ao bucket). O envio confere o ETag devolvido com o MD5 do
# arquivo, e só depois de um envio conferido o script apaga do bucket o que passou de
# BACKUP_DIAS_NO_BUCKET dias (30). Falha do bucket é avisada no log e não segura o deploy.
# Para testar a conexão sem mandar dado de cliente: docker compose ... exec backup sh /backup.sh testar-bucket
set -eu

# A cópia tem dado de cliente e hash de senha, e o contêiner roda como root: sem isto o arquivo
# nasce 0644 no disco do VPS, legível por qualquer usuário da máquina. O mesmo vale para o
# /tmp/email.json, que carrega a cópia inteira em base64.
umask 077

LIMITE_DO_ANEXO=28000000

# Falha fechado só no e-mail: ligado sem senha, nada sai em claro para a caixa de entrada, mas a cópia no
# disco continua (e o Deploy, que depende dela, não fica preso por falta de uma variável).
if [ -n "${BACKUP_PARA:-}" ] && [ -z "${BACKUP_SENHA:-}" ]; then
  echo "AVISO: BACKUP_PARA está ligado e BACKUP_SENHA está vazia; a cópia fica só no disco, sem e-mail." >&2
  BACKUP_PARA=""
fi

enviar() {
  original="$1"
  cifrado="$original.enc"
  # A senha vai pelo ambiente do openssl, e não pela linha de comando (visível em ps).
  if ! openssl enc -aes-256-cbc -pbkdf2 -iter 600000 -salt -pass env:BACKUP_SENHA -in "$original" -out "$cifrado"; then
    rm -f "$cifrado"
    echo "criptografia da cópia falhou; nada foi enviado" >&2
    return 0
  fi
  arquivo="$cifrado"
  nome=$(basename "$arquivo")
  tamanho=$(wc -c < "$arquivo")
  dia=$(TZ=America/Sao_Paulo date +%d/%m/%Y)
  corpo=/tmp/email.json
  if [ "$tamanho" -le "$LIMITE_DO_ANEXO" ]; then
    {
      printf '{"from":"%s","to":["%s"],"subject":"Cópia do banco do Avalia Sales - %s",' "$RESEND_REMETENTE" "$BACKUP_PARA" "$dia"
      printf '"text":"Cópia diária do banco, criptografada (AES-256-CBC), %s bytes. Restauração em docs/OPERACAO.md.",' "$tamanho"
      printf '"attachments":[{"filename":"%s","content":"' "$nome"
      base64 -w0 "$arquivo"
      printf '"}]}'
    } > "$corpo"
  else
    printf '{"from":"%s","to":["%s"],"subject":"Cópia do banco do Avalia Sales - %s - SEM ANEXO","text":"A cópia tem %s bytes e passou do limite do e-mail. Ela ficou só no disco do VPS (%s). Configure outro destino externo."}' \
      "$RESEND_REMETENTE" "$BACKUP_PARA" "$dia" "$tamanho" "$(basename "$original")" > "$corpo"
  fi
  if curl -fsS --retry 3 --retry-all-errors -o /dev/null https://api.resend.com/emails \
      -H "Authorization: Bearer $RESEND_CHAVE_API" -H "Content-Type: application/json" \
      -H "Idempotency-Key: backup-$nome" --data-binary @"$corpo"; then
    echo "cópia enviada para $BACKUP_PARA"
  else
    echo "envio da cópia falhou" >&2
  fi
  rm -f "$corpo" "$cifrado"
}

bucket_ligado() {
  [ -n "${S3_ENDPOINT:-}" ] && [ -n "${S3_BUCKET:-}" ] && [ -n "${S3_KEY_ID:-}" ] && [ -n "${S3_APP_KEY:-}" ]
}

# s3 METODO CAMINHO [argumentos do curl]: pedido assinado (SigV4) ao bucket. As chaves vão por
# stdin ao curl, e não pela linha de comando (visível em ps). "auto" é a região do R2; o B2 e
# outros aceitam o mesmo nome. A carga vai sem hash (HTTPS), e a integridade é conferida pelo ETag.
s3() {
  metodo="$1"
  caminho="$2"
  shift 2
  printf 'user = "%s:%s"\n' "$S3_KEY_ID" "$S3_APP_KEY" |
    curl -sS --retry 3 --retry-all-errors --max-time 900 -K - --aws-sigv4 "aws:amz:auto:s3" \
      -H "x-amz-content-sha256: UNSIGNED-PAYLOAD" -X "$metodo" "$@" "${S3_ENDPOINT%/}/$S3_BUCKET$caminho"
}

md5_de() {
  openssl dgst -md5 -r "$1" | cut -d' ' -f1
}

# Criptografa, envia ao bucket e confere. Devolve 0 só com o envio conferido.
guardar_no_bucket() {
  original="$1"
  bucket_ligado || return 1
  if [ -z "${BACKUP_SENHA:-}" ]; then
    echo "AVISO: o bucket está ligado e BACKUP_SENHA está vazia; a cópia não vai ao bucket em claro." >&2
    return 1
  fi
  cifrado="$original.bucket.enc"
  cabecalhos=/tmp/s3-cabecalhos
  if ! openssl enc -aes-256-cbc -pbkdf2 -iter 600000 -salt -pass env:BACKUP_SENHA -in "$original" -out "$cifrado"; then
    rm -f "$cifrado"
    echo "criptografia da cópia falhou; nada foi ao bucket" >&2
    return 1
  fi
  nome="$(basename "$original").enc"
  esperado=$(md5_de "$cifrado")
  codigo=$(s3 PUT "/$nome" -T "$cifrado" -D "$cabecalhos" -o /dev/null -w '%{http_code}') || codigo=000
  recebido=$(tr -d '"\r' < "$cabecalhos" 2>/dev/null | sed -n 's/^[Ee][Tt]ag: *//p' | tail -n 1)
  rm -f "$cifrado" "$cabecalhos"
  if [ "$codigo" = 200 ] && [ "$recebido" = "$esperado" ]; then
    echo "cópia no bucket: $nome"
    return 0
  fi
  echo "envio ao bucket falhou ou não conferiu (HTTP $codigo, ETag '$recebido', esperado '$esperado')" >&2
  return 1
}

# Só roda depois de um envio conferido de hoje, para nunca esvaziar o bucket quando as cópias pararam.
limpar_bucket() {
  corte=$(date -u -d "${BACKUP_DIAS_NO_BUCKET:-30} days ago" +%Y%m%d) || return 0
  [ -n "$corte" ] || return 0
  s3 GET "?list-type=2&prefix=avalia_sales-" 2>/dev/null | grep -o '<Key>[^<]*</Key>' | sed 's/<[^>]*>//g' |
    while read -r chave; do
      dia=$(printf '%s' "$chave" | sed -n 's/^avalia_sales-\([0-9]\{8\}\)T.*/\1/p')
      if [ -n "$dia" ] && [ "$dia" -lt "$corte" ]; then
        s3 DELETE "/$chave" -o /dev/null && echo "removida do bucket: $chave"
      fi
    done || true
}

# Sem dado de cliente: grava um arquivo de teste, lê de volta, compara e apaga.
testar_bucket() {
  bucket_ligado || { echo "S3_ENDPOINT, S3_BUCKET, S3_KEY_ID e S3_APP_KEY não estão todos definidos" >&2; return 1; }
  chave="teste-de-conexao-$(date -u +%Y%m%dT%H%M%SZ).txt"
  printf 'teste de conexão do backup do Avalia Sales\n' > /tmp/s3-teste
  codigo=$(s3 PUT "/$chave" -T /tmp/s3-teste -o /dev/null -w '%{http_code}') || codigo=000
  [ "$codigo" = 200 ] || { echo "envio de teste falhou (HTTP $codigo)" >&2; rm -f /tmp/s3-teste; return 1; }
  s3 GET "/$chave" -o /tmp/s3-lido || { echo "leitura de teste falhou" >&2; rm -f /tmp/s3-teste; return 1; }
  if cmp -s /tmp/s3-teste /tmp/s3-lido; then resultado=0; echo "bucket ok: enviou, leu de volta e a cópia é idêntica"; else resultado=1; echo "o que voltou do bucket é diferente do enviado" >&2; fi
  s3 DELETE "/$chave" -o /dev/null || echo "AVISO: não consegui apagar $chave do bucket" >&2
  rm -f /tmp/s3-teste /tmp/s3-lido
  return $resultado
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
  # A cópia só vale se o pg_restore consegue ler o índice dela e o razão está lá:
  # arquivo truncado ou corrompido falha aqui, e não no dia de restaurar.
  if pg_dump -Fc -f "$arquivo.parcial" && pg_restore --list "$arquivo.parcial" | grep -q "TABLE DATA public lancamentos"; then
    mv "$arquivo.parcial" "$arquivo"
    echo "backup gravado: $arquivo"
    if [ -n "${BACKUP_PARA:-}" ] && [ -n "${RESEND_CHAVE_API:-}" ] && [ -n "${RESEND_REMETENTE:-}" ]; then
      enviar "$arquivo"
    fi
    if bucket_ligado && guardar_no_bucket "$arquivo"; then
      limpar_bucket
    fi
  else
    rm -f "$arquivo.parcial"
    echo "backup falhou" >&2
    return 1
  fi
  find /backups -name 'avalia_sales-*.dump' -mtime +"${BACKUP_DIAS:-14}" -delete
}

if [ "${1:-}" = testar-bucket ]; then
  testar_bucket
  exit $?
fi

# "agora" é a cópia antes de cada publicação: falhar aqui segura o deploy.
if [ "${1:-}" = agora ]; then
  copiar
  exit $?
fi

while true; do
  esperar
  copiar || true
done
