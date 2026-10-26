#!/bin/sh
# Teste do fluxo do backup.sh com e-mail e bucket ligados juntos, sem Postgres, rede ou Resend:
# pg_dump, pg_restore e curl são trocados por falsos. Pega o defeito em que o envio por e-mail
# trocava a variável da cópia e o bucket recebia o caminho do anexo já apagado.
# Uso: sh infra/backup/teste-do-fluxo.sh
set -eu

RAIZ=$(cd "$(dirname "$0")/../.." && pwd)
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT
mkdir -p "$TMP/bin" "$TMP/backups"

cat > "$TMP/bin/pg_dump" <<'FALSO'
#!/bin/sh
while [ $# -gt 0 ]; do if [ "$1" = -f ]; then shift; head -c 50000 /dev/urandom > "$1"; fi; shift; done
FALSO
cat > "$TMP/bin/pg_restore" <<'FALSO'
#!/bin/sh
echo "1; 0 1 TABLE DATA public lancamentos avalia_dono"
FALSO
# base64 sem -w0 (macOS): o mesmo resultado, em uma linha, pelo openssl.
cat > "$TMP/bin/base64" <<'FALSO'
#!/bin/sh
[ "$1" = -w0 ] && shift
openssl base64 -A -in "$1"
FALSO
# curl falso: o Resend só confirma; o "bucket" devolve 200 e o ETag certo (MD5 do arquivo enviado) e guarda o nome.
cat > "$TMP/bin/curl" <<FALSO
#!/bin/sh
for a in "\$@"; do
  case "\$a" in *api.resend.com*) echo email >> "$TMP/chamadas"; exit 0 ;; esac
done
cabecalhos=""; arquivo=""; metodo=""; alvo=""
while [ \$# -gt 0 ]; do
  case "\$1" in
    -D) cabecalhos="\$2"; shift ;;
    -T) arquivo="\$2"; shift ;;
    -X) metodo="\$2"; shift ;;
    -o|-w|-H) shift ;;
    http*) alvo="\$1" ;;
  esac
  shift
done
if [ "\$metodo" = PUT ] && [ -n "\$arquivo" ]; then
  [ -f "\$arquivo" ] || { echo "curl falso: arquivo inexistente \$arquivo" >&2; exit 26; }
  echo "put \$alvo" >> "$TMP/chamadas"
  printf 'HTTP/1.1 200 OK\r\nETag: "%s"\r\n\r\n' "\$(openssl dgst -md5 -r "\$arquivo" | cut -d' ' -f1)" > "\$cabecalhos"
  printf 200
fi
FALSO
chmod +x "$TMP/bin/"*

sed "s#/backups#$TMP/backups#g" "$RAIZ/infra/backup/backup.sh" > "$TMP/backup.sh"
saida=$(PATH="$TMP/bin:$PATH" BACKUP_PARA=a@b.c RESEND_CHAVE_API=x RESEND_REMETENTE=r@b.c BACKUP_SENHA=senha-do-teste \
  S3_ENDPOINT=https://s3.exemplo S3_BUCKET=bucket S3_KEY_ID=k S3_APP_KEY=s sh "$TMP/backup.sh" agora 2>&1) || { echo "$saida"; echo "FALHOU: backup.sh agora saiu com erro" >&2; exit 1; }

falhou=0
confere() { printf '%s' "$saida" | grep -q "$1" || { echo "FALHOU: esperava '$1' na saída" >&2; falhou=1; }; }
confere "backup gravado:"
confere "cópia enviada para a@b.c"
confere "cópia no bucket: avalia_sales-.*\.dump\.enc"
grep -q '^email$' "$TMP/chamadas" || { echo "FALHOU: o e-mail não saiu" >&2; falhou=1; }
grep -q '^put https://s3.exemplo/bucket/avalia_sales-.*\.dump\.enc$' "$TMP/chamadas" || { echo "FALHOU: o bucket não recebeu o .dump.enc" >&2; falhou=1; }
# Nada de cópia cifrada sobrando no disco.
ls "$TMP/backups" | grep -q '\.enc$' && { echo "FALHOU: sobrou arquivo .enc no disco" >&2; falhou=1; }

[ "$falhou" = 0 ] && echo "ok: e-mail e bucket no mesmo fluxo"
exit "$falhou"
