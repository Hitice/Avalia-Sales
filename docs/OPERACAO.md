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
   `DOMINIO` no ambiente; variável `DEPLOY_ATIVO=true` no repositório (sem
   ela, o workflow Deploy não roda, para não ficar na fila sem runner).
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
`versao`, pelo SHA completo de 40 caracteres (`git rev-parse <commit>`): o
abreviado falha no checkout. Pela linha de comando:
`gh workflow run deploy.yml --ref main -f versao=$(git rev-parse <commit>)`.
As imagens desse commit ainda estão no VPS por 7 dias; depois disso,
o workflow compila de novo.

## Backup e restauração

- O contêiner `backup` grava `avalia_sales-AAAAMMDDTHHMMSSZ.dump` às 02:00 de
  Brasília em `PASTA_DE_BACKUP` e apaga os mais velhos que `BACKUP_DIAS`.
- Com `BACKUP_PARA` e o Resend configurado, a cópia também vai anexada para essa
  caixa, que é o destino fora do VPS. Acima de 28 MB o e-mail sai sem anexo,
  avisando: aí é hora de outro destino (armazenamento de objetos).
- Cópia na hora: `docker compose -f docker-compose.producao.yml --env-file /srv/avalia-sales/.env exec backup sh /backup.sh agora`.
- Teste a restauração todo mês, num banco à parte:

  ```bash
  docker compose -f docker-compose.producao.yml --env-file /srv/avalia-sales/.env exec postgres \
    sh -c 'createdb -U avalia_dono ensaio && pg_restore -U avalia_dono -d ensaio /backups/<arquivo>.dump'
  ```

  (monte a pasta de backup no `postgres` para esse ensaio ou copie o arquivo
  para dentro com `docker compose cp`).
- Restaurar a produção: pare `api`, `worker` e `nginx`, recrie o banco a
  partir do dump com `pg_restore --clean --if-exists`, e suba de novo.

## Reservar códigos e importar placas

Os dois comandos servem a qualquer revendedor que já venda placas em outro
sistema, como o Sales da Avalia. Rodam pelo contêiner da API:

```bash
docker compose -f docker-compose.producao.yml --env-file /srv/avalia-sales/.env run --rm \
  -v /srv/avalia-sales/importacao:/importacao api --reservar-codigos --arquivo=/importacao/codigos.csv --origem="Sales Avalia"
```

- Reservar códigos - CSV com o código na primeira coluna (com ou sem
  cabeçalho; linha que não é código conta como inválida). O sorteio de lote
  nunca gera código reservado. Código que já foi emitido na plataforma sai como
  conflito, e o comando termina com código de saída 2.
- Importar placas - `--importar-placas --revendedor=<CNPJ ou id> --arquivo=...`,
  primeiro com `--ensaio`, que desfaz tudo no fim e só mostra os totais para
  conferir com o relatório de origem, ao centavo. Tudo ou nada: uma linha
  recusada desfaz o arquivo inteiro e a lista diz qual linha e por quê.

Formato do arquivo de placas (UTF-8, vírgula, cabeçalho na primeira linha,
colunas em qualquer ordem; só `codigo` e `situacao` são obrigatórias):

| Coluna | Conteúdo |
| --- | --- |
| `codigo` | Código da placa (normalizado como na leitura: minúscula, I e L por 1, O por 0) |
| `situacao` | `em_branco`, `ativa`, `suspensa` ou `baixada` |
| `destino` | Endereço para onde a placa leva (conferido como no balcão) |
| `vendida_em` | Data da venda, `2026-01-10` (meia-noite em Brasília) ou data e hora com fuso. Obrigatória para placa ativa ou suspensa |
| `vence_em` | Vencimento, no mesmo formato |
| `valor_cents` | Valor cobrado, em centavos, sem vírgula |
| `custo_cents` | Custo da placa, em centavos |
| `cliente_nome`, `cliente_responsavel`, `cliente_telefone`, `cliente_documento` | O cliente; um por documento, senão por telefone, senão por nome |
| `vendedor_email` | Login do vendedor na plataforma; sem ele, a venda fica com o dono |
| `produto` | Chave do produto no catálogo da Avalia (`placa-avaliacao-google`); sem ela, a venda vai para o produto genérico "Placa importada" |
| `produto_preco_cents` | Preço da revenda para o produto, se ela ainda não o tinha |
| `comissao_devida_cents` | Comissão de venda paga ainda não repassada; vira saldo do vendedor, que precisa ser da equipe |
| `total_leituras` | Leituras acumuladas |

A placa importada entra num lote próprio, já ativado no revendedor; a venda
importada mantém o valor de origem, fica sem cobrança e sem comissão (já
acertada lá), e o código importado deixa de ser reserva.

### Do Sales da Avalia

O backup diário do Sales (`avalia:exportar`, que chega por e-mail) é a origem.
O conversor só lê o arquivo, sem tocar no Sales:

```sh
python3 -I infra/migracao/converter_do_sales.py avalia-AAAA-MM-DD-HHMM.sql.gz --saida placas.csv \
  --socio <e-mail do sócio 1 no Sales> --socio <e-mail do sócio 2 no Sales> \
  --vendedor <e-mail do sócio 1 no Sales>=<login dele na plataforma> \
  --vendedor <e-mail do sócio 2 no Sales>=<login dele na plataforma>
```

Ele imprime os totais (placas por situação, valor vendido, vendas não pagas,
comissão devida por vendedor) para conferir com as telas do Sales. As regras
de lá - comissão de 25% sobre o valor, só de venda paga, nunca de sócio; venda
paga é a com `pix_pago_em` ou anterior a 11/10/2026; datas em UTC. Venda sem
placa (a da Lia) fica de fora. Cada vendedor do Sales com comissão devida
precisa de login na equipe antes do `--importar-placas`, com o e-mail do Sales
ou o do `--vendedor`.

Até a virada, só `--ensaio`: o Sales segue vendendo, e as placas impressas
leem pelo `avaliaone.com.br/q/`, que nada daqui alcança.

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
