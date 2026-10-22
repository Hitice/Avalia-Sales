# Avalia Sales - operação

Como instalar, publicar, voltar, fazer backup e restaurar a plataforma no VPS.
Arquitetura e decisões no [SDD](SDD.md); cada decisão estrutural em
[docs/adr](adr/README.md).

## Peças

| Contêiner | Papel | Porta |
| --- | --- | --- |
| `nginx` | Front (React compilado), TLS, `/api/` (com WebSocket do hub), `/q/` e `/webhooks/` para a API | 80 e 443, as únicas publicadas |
| `api` | API e hub de avisos | 8080, só na rede do compose |
| `worker` | Webhooks, retomadas, conferência de cobrança, e-mails, ERP | - |
| `migrar` | Migrations como dono do banco; termina antes de `api` e `worker` | - |
| `postgres` | PostgreSQL 17, papel `avalia_app` sem superusuário e sem BYPASSRLS | só na rede do compose |
| `certbot` | Renova o certificado do Let's Encrypt duas vezes por dia | - |
| `backup` | `pg_dump` diário, guarda `BACKUP_DIAS` dias | - |

## Primeira instalação

1. VPS Linux com Docker e o plugin Compose. DNS do domínio (por exemplo
   `sales.avaliaone.com.br`) apontando para o IP do VPS. Portas 80 e 443
   abertas; nada mais.
2. Runner próprio do GitHub Actions no VPS, com os rótulos `self-hosted`,
   `linux` e `avalia-sales`, rodando com um usuário do grupo `docker`.
3. No GitHub: ambiente `producao` com aprovação obrigatória; variável
   `DOMINIO` no ambiente.
4. No VPS, `/srv/avalia-sales/.env` a partir do `.env.example`, com
   `chmod 600`. Gere os segredos com `openssl rand -base64 48`.
5. Primeira subida (rode o workflow Deploy, ou à mão na pasta do checkout):

   ```bash
   docker compose -f docker-compose.producao.yml --env-file /srv/avalia-sales/.env up -d --build
   ```

   O Nginx sobe com um certificado provisório, só para responder o desafio do
   Let's Encrypt.
6. Certificado de verdade (uma vez; depois o `certbot` renova sozinho):

   ```bash
   set -a; . /srv/avalia-sales/.env; set +a
   docker compose -f docker-compose.producao.yml --env-file /srv/avalia-sales/.env run --rm --entrypoint sh certbot -c \
     "rm -rf /etc/letsencrypt/live/$DOMINIO && certbot certonly --webroot -w /var/www/certbot \
      -d $DOMINIO --cert-name $DOMINIO --email $EMAIL_DO_CERTIFICADO --agree-tos --no-eff-email"
   docker compose -f docker-compose.producao.yml --env-file /srv/avalia-sales/.env exec nginx nginx -s reload
   ```

7. Operadora e revendedores:

   ```bash
   docker compose -f docker-compose.producao.yml --env-file /srv/avalia-sales/.env run --rm api \
     --nova-operadora --nome="Operação Avalia" --email=... --senha=...
   ```

8. Webhooks - no Pagar.me, `https://DOMINIO/webhooks/pagarme/<PAGARME_SEGREDO_DO_WEBHOOK>`;
   na D4Sign, o endereço vai sozinho em cada envio.

## Publicar

Merge no `main` com o CI verde dispara o workflow Deploy, que espera a
aprovação no ambiente `producao`. Ele compila as imagens marcadas com o commit
(`VERSAO`), sobe (o `migrar` roda antes da API) e confere
`https://DOMINIO/api/health` por até 150 s.

- Migration só aditiva: a versão anterior continua lendo o banco depois da
  migração, e é isso que torna a volta segura.
- Leitura do QR (`/q/`) não pode cair: o compose troca API e Worker em
  segundos, e o Nginx segura a conexão até a API nova responder.

## Voltar

Rode o workflow Deploy à mão (`workflow_dispatch`) com o commit anterior em
`versao`. As imagens desse commit ainda estão no VPS por 7 dias; depois disso,
o workflow compila de novo.

## Backup e restauração

- O contêiner `backup` grava `avalia_sales-AAAAMMDDTHHMMSSZ.dump` por dia em
  `PASTA_DE_BACKUP` e apaga os mais velhos que `BACKUP_DIAS`. Copie a pasta
  para fora do VPS (outro servidor ou armazenamento de objetos).
- Teste a restauração todo mês, num banco à parte:

  ```bash
  docker compose -f docker-compose.producao.yml --env-file /srv/avalia-sales/.env exec postgres \
    sh -c 'createdb -U avalia_dono ensaio && pg_restore -U avalia_dono -d ensaio /backups/<arquivo>.dump'
  ```

  (monte a pasta de backup no `postgres` para esse ensaio ou copie o arquivo
  para dentro com `docker compose cp`).
- Restaurar a produção: pare `api`, `worker` e `nginx`, recrie o banco a
  partir do dump com `pg_restore --clean --if-exists`, e suba de novo.

## Logs e erros

- `docker compose -f docker-compose.producao.yml logs -f api worker`.
- Sentry com `SENTRY_DSN` (API e Worker) e `SENTRY_DSN_DO_FRONT` (front, na
  compilação). Sem dado pessoal: nem corpo, nem cookie, nem IP.
- Fila parada - e-mails sem Resend esperam na tabela `emails`; pedido de venda
  sem Protheus espera em `envios_ao_erp` (tela ERP da operadora); webhook que
  falhou 10 vezes fica em `eventos_externos` com o último erro.

## Segredos

Só no `.env` do VPS, fora do repositório. Troca de `JWT_CHAVE` derruba os
tokens de acesso (15 minutos) e nada mais: o refresh fica no banco. Troca do
segredo do webhook do Pagar.me pede o endereço novo no painel do Pagar.me.
