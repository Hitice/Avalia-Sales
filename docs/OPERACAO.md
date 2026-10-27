# Avalia Sales - operação

Como instalar, publicar, voltar, fazer backup e restaurar a plataforma no VPS.
Arquitetura e decisões no [SDD](SDD.md); cada decisão estrutural em
[docs/adr](adr/README.md).

## Peças

| Contêiner | Papel | Porta |
| --- | --- | --- |
| `nginx` | Front (React compilado), TLS, `/api/` (com WebSocket do hub), `/q/`, `/l/` e `/webhooks/` para a API | 80 e 443, as únicas publicadas |
| `api` | API e hub de avisos | 8080, só na rede do compose |
| `worker` | Webhooks, retomadas, reconsulta de cobrança e de assinatura, contrato e distrato, e-mails, ERP | - |
| `migrar` | Migrations como dono do banco; termina antes de `api` e `worker` | - |
| `gotenberg` | PDF do contrato e do distrato, chamado só pelo Worker. O Chromium não abre endereço interno nem externo (`CHROMIUM_DENY_PRIVATE_IPS`, `CHROMIUM_DENY_PUBLIC_IPS`); o contêiner fica na rede padrão do compose | 3000, só na rede do compose |
| `postgres` | PostgreSQL 17, papel `avalia_app` sem superusuário e sem BYPASSRLS | só na rede do compose |
| `certbot` | Renova o certificado do Let's Encrypt duas vezes por dia | - |
| `backup` | `pg_dump` diário, guarda `BACKUP_DIAS` dias no disco e envia a cópia cifrada ao bucket | - |

## Primeira instalação

1. VPS Linux com Docker e o plugin Compose. DNS do domínio (por exemplo
   `sales.avaliaone.com.br`) apontando para o IP do VPS. No firewall, só 80,
   443 e a porta do SSH abertas.
2. Runner próprio do GitHub Actions no VPS, com os rótulos `self-hosted`,
   `linux` e `avalia-sales`, rodando com um usuário do grupo `docker`.
3. No GitHub: ambiente `producao` com aprovação obrigatória; variável
   `DOMINIO` no ambiente; variável `DEPLOY_ATIVO=true` no repositório (sem
   ela, o workflow Deploy não roda, para não ficar na fila sem runner).
4. No VPS, `/srv/avalia-sales/.env` a partir do `.env.example`, com
   `chmod 600`. `DOMINIO` também vai no `.env`: o Compose lê dele (a variável
   do GitHub serve só ao `curl` de saúde do Deploy). Segredos:
   - Senhas do Postgres e `JWT_CHAVE` - `openssl rand -base64 48`.
   - `CHAVE_DOS_EMAILS` - `openssl rand -base64 32` (exatamente 32 bytes; com
     outro tamanho a API e o Worker não sobem).
   - `PAGARME_SEGREDO_DO_WEBHOOK` - `openssl rand -hex 32` (vai na URL do
     webhook, e base64 tem `/` e `+`).

   `AVALIA_REPRESENTANTE` e `AVALIA_CARGO_DO_REPRESENTANTE` saem impressos no
   contrato e no distrato; ficam nas variáveis do ambiente `producao` do GitHub
   (`gh variable set AVALIA_REPRESENTANTE --env producao --body "..."`), que o
   Deploy entrega ao Compose e valem mais que o `.env`. Quem sobe o Compose à
   mão (seções Pagar.me e Segredos) precisa delas e das chaves do Turnstile
   também no `.env`, senão o contêiner recriado perde os valores. O PDF é
   feito pelo serviço `gotenberg` do Compose, sem porta publicada; a primeira
   subida baixa a imagem.
5. Primeira subida (rode o workflow Deploy, ou à mão na pasta do checkout):

   ```bash
   docker compose -f docker-compose.producao.yml --env-file /srv/avalia-sales/.env up -d --build
   ```

   O Nginx sobe com um certificado provisório, só para responder o desafio do
   Let's Encrypt.
6. Certificado definitivo do Let's Encrypt (uma vez; depois o `certbot` renova sozinho):

   ```bash
   set -a; . /srv/avalia-sales/.env; set +a
   docker compose -f docker-compose.producao.yml --env-file /srv/avalia-sales/.env run --rm --entrypoint sh certbot -c \
     "rm -rf /etc/letsencrypt/live/$DOMINIO && certbot certonly --webroot -w /var/www/certbot \
      -d $DOMINIO --cert-name $DOMINIO --email $EMAIL_DO_CERTIFICADO --agree-tos --no-eff-email"
   docker compose -f docker-compose.producao.yml --env-file /srv/avalia-sales/.env exec nginx nginx -s reload
   ```

7. Operadora e revendedores. Todo `run --rm api` roda na pasta do checkout
   (`/home/deploy/actions-runner/_work/Avalia-Sales/Avalia-Sales`) com a
   versão que está no ar; sem `VERSAO`, o Compose usa a imagem `local`, que
   pode ser antiga:

   ```bash
   export VERSAO=$(docker inspect --format '{{.Config.Image}}' avalia-sales-api-1 | cut -d: -f2)
   ```


   ```bash
   timeout 600 docker compose -f docker-compose.producao.yml --env-file /srv/avalia-sales/.env run --rm -T api \
     --nova-operadora --nome="Operação Avalia" --email=... --senha=...
   ```

   Crie uma operadora só: o comando não impede uma segunda. A revenda se
   cadastra pela tela; pela linha de comando, `--novo-revendedor --nome=...
   --email=... --senha=...` no mesmo formato.

   Para dar a outro login um e-mail que já está em uso (por exemplo, o dono de
   uma revenda passa a usar outro e-mail e o antigo fica para a operadora),
   troque o e-mail antes, sem apagar nada:

   ```bash
   timeout 600 docker compose -f docker-compose.producao.yml --env-file /srv/avalia-sales/.env run --rm -T api \
     --trocar-email --de=comercial@avaliaone.com.br --para=novo-dono@avaliaone.com.br
   ```

8. Webhooks - no Pagar.me, `https://DOMINIO/webhooks/pagarme/<PAGARME_SEGREDO_DO_WEBHOOK>`;
   na D4Sign, o endereço vai sozinho em cada envio.

## Pagar.me em produção

1. No `/srv/avalia-sales/.env`: `PAGARME_CHAVE_SECRETA` (`sk_`),
   `PAGARME_CHAVE_PUBLICA` (`pk_`, liga o cartão na Nova venda),
   `PAGARME_RECEBEDOR_AVALIA` (o recebedor principal da conta da Avalia, `rp_` ou
   `re_`, que fica com a parte da Avalia)
   e `PAGARME_SEGREDO_DO_WEBHOOK`. Sem a chave secreta, o recebedor ou o
   segredo do webhook, o Compose não sobe. A chave não vai para o GitHub nem
   para o chat.
2. Recriar API e Worker com o `.env` novo, na pasta do checkout e com a
   versão que está no ar (sem `VERSAO`, o Compose sobe a imagem `local`). O
   `.env` precisa ter também as variáveis que o Deploy traz do GitHub
   (Primeira instalação, passo 4). O Nginx resolve o endereço da API só ao
   subir, então recarregue depois:

   ```bash
   export VERSAO=$(docker inspect --format '{{.Config.Image}}' avalia-sales-api-1 | cut -d: -f2)
   docker compose -f docker-compose.producao.yml --env-file /srv/avalia-sales/.env up -d api worker
   docker compose -f docker-compose.producao.yml --env-file /srv/avalia-sales/.env exec nginx nginx -s reload
   ```

3. Conferir a chave e os recebedores, sem cobrar ninguém:

   ```bash
   timeout 600 docker compose -f docker-compose.producao.yml --env-file /srv/avalia-sales/.env run --rm -T api --conferir-pagarme
   ```

   Mostra se a chave é de teste ou de produção, se o cartão está ligado e a
   situação de cada recebedor (`active` recebe). Sai com erro se a chave for
   recusada ou se algum recebedor não estiver ativo.
4. No painel do Pagar.me, webhook para
   `https://DOMINIO/webhooks/pagarme/<PAGARME_SEGREDO_DO_WEBHOOK>` com os
   eventos de pedido e de cobrança (`order.*` e `charge.*`). O Worker
   reconsulta o pedido em todo evento, então assinar a mais não muda nada.
5. Cada revenda precisa de um recebedor próprio, criado no painel do
   Pagar.me; a operadora cola o id (`rp_` ou `re_`) em Revendedores. A
   revenda Avalia ainda usa o recebedor principal da Avalia: a cobrança vai
   numa regra só e há um saque só. A decisão é um recebedor próprio para ela
   (PDD, seção 13), que falta criar. O pedido de placas da revenda Avalia não
   passa pelo Pagar.me: a operadora registra o pagamento em Pedidos. Transferência automática semanal
   (ou mensal) em cada recebedor: cada saque custa R$ 3,67, e a diária cobra
   isso todo dia.
6. Primeira venda real de valor baixo no Pix, para ver o split no extrato e
   o pedido pago no painel.

## Publicar

Push no `main` com o CI verde dispara o workflow Deploy, que espera a
aprovação no ambiente `producao`. Commit só de `docs/`, Markdown ou
`.claude/` não roda o CI nem o Deploy. O Deploy compila as imagens marcadas
com o commit (`VERSAO`), faz uma cópia do banco pelo contêiner `backup` (com o
contêiner de pé, cópia que falha segura a publicação; parado, o Deploy só
avisa e publica sem cópia), sobe (o `migrar` roda antes da API) e confere
`https://DOMINIO/api/health` por até 150 s. Só publica o CI de um push no
`main` deste repositório: pull request de fork não chega ao runner do VPS.

- Migration só aditiva: a versão anterior continua lendo o banco depois da
  migração, e é isso que torna a volta segura. É regra de revisão: nenhum
  teste nem job confere que a migration não remove ou renomeia coluna.
- Leitura do QR (`/q/`, `/l/`) - enquanto a API reinicia, o Nginx responde a
  página de espera, que recarrega em 3 s. O próprio Nginx é recriado em cada
  publicação (imagem com a tag nova), e as portas 80 e 443 ficam fechadas
  por alguns segundos.

## Voltar

Rode o workflow Deploy à mão (`workflow_dispatch`) com o commit anterior em
`versao`, pelo SHA completo de 40 caracteres (`git rev-parse <commit>`): o
abreviado falha no checkout. Pela linha de comando:
`gh workflow run deploy.yml --ref main -f versao=$(git rev-parse <commit>)`.
O workflow sempre compila o commit pedido, com o cache de build dos últimos 7
dias. O VPS guarda só a imagem no ar e a anterior.

## Backup e restauração

- O contêiner `backup` grava `avalia_sales-AAAAMMDDTHHMMSSZ.dump` às 02:00 de
  Brasília em `PASTA_DE_BACKUP` e apaga os mais velhos que `BACKUP_DIAS`.
- Sem `BACKUP_PARA`, as cópias ficam só no disco do VPS: protegem de engano
  (migration, exclusão), não da perda do servidor. Com `BACKUP_PARA` e o Resend,
  a cópia vai anexada por e-mail até 28 MB, sempre criptografada
  (`avalia_sales-<data>.dump.enc`, AES-256-CBC com PBKDF2) com a senha de
  `BACKUP_SENHA`. `BACKUP_SENHA` é obrigatória com `BACKUP_PARA`: sem ela o
  e-mail é desligado, com aviso no log, e a cópia no disco continua. Guarde a senha fora do
  VPS (gerenciador de senhas): perdida, as cópias por e-mail não abrem. O
  arquivo no disco do VPS não é criptografado. O destino fora do VPS é o bucket descrito abaixo.
- Cópia fora do VPS num bucket compatível com S3; em produção, o Cloudflare R2.
  Com `S3_ENDPOINT`, `S3_BUCKET`, `S3_KEY_ID`, `S3_APP_KEY` e `BACKUP_SENHA` no `.env`,
  cada cópia é criptografada (a mesma senha e o mesmo formato do anexo por e-mail),
  enviada e conferida pelo ETag; só então o script apaga do bucket o que passou de
  `BACKUP_DIAS_NO_BUCKET` dias (30). A cópia de cada publicação (`agora`) também vai ao
  bucket e ao e-mail. Sem `BACKUP_SENHA` nada vai ao bucket. Falha do bucket não segura
  o deploy e não manda e-mail: fica só no log do contêiner `backup`
  (`docker compose ... logs backup | grep bucket`). Teste a conexão sem dado de cliente:

  ```bash
  docker compose -f docker-compose.producao.yml --env-file /srv/avalia-sales/.env exec backup sh /backup.sh testar-bucket
  ```

  No R2 crie o bucket e um token de API de leitura e escrita restrito a ele; o endereço
  é `https://<account-id>.r2.cloudflarestorage.com`. Para restaurar, baixe o `.dump.enc`
  do painel do bucket e use o `openssl enc -d` abaixo.
- O `backup.sh` grava em `.parcial` e só renomeia depois que o
  `pg_restore --list` lê o arquivo e acha os dados de `lancamentos`. Cópia
  cortada ou vazia sai com erro e não apaga as anteriores.
- Abrir a cópia recebida por e-mail (salve o anexo e use a mesma senha):

  ```bash
  read -rs BACKUP_SENHA && export BACKUP_SENHA
  openssl enc -d -aes-256-cbc -pbkdf2 -iter 600000 -pass env:BACKUP_SENHA \
    -in avalia_sales-<data>.dump.enc -out avalia_sales.dump
  pg_restore --list avalia_sales.dump | grep "TABLE DATA public lancamentos"
  ```

  Senha errada ou arquivo corrompido falha com `bad decrypt`. O `.dump`
  resultante restaura como qualquer outro, com `pg_restore` (abaixo).
- Cópia na hora: `docker compose -f docker-compose.producao.yml --env-file /srv/avalia-sales/.env exec backup sh /backup.sh agora`.
- Teste a restauração todo mês, num banco à parte, fora do horário de uso: a
  restauração divide CPU e disco com a produção. Na pasta do checkout, com o dump mais
  novo de `/srv/avalia-sales/backups`. O contêiner `backup` já tem a pasta em `/backups`,
  as ferramentas do Postgres 17 e o acesso ao banco (`PGHOST`, `PGUSER`, `PGPASSWORD`);
  o dump é `0600` do root (`umask 077`), e o contêiner o lê como root. A linha imprime as
  contagens entre `CONTAGEM` e `FIM`; todas devem ser maiores que zero e sem erro de
  tabela antes delas:

  ```bash
  C="docker compose -f docker-compose.producao.yml --env-file /srv/avalia-sales/.env"; $C exec -T backup sh -c "createdb ensaio && pg_restore -d ensaio --no-owner /backups/<arquivo>.dump 2>&1 | tail -3; echo CONTAGEM; psql -d ensaio -Atc \"select (select count(*) from lancamentos), (select count(*) from vendas), (select count(*) from revendedores)\"; dropdb ensaio; echo FIM"
  ```

  Só o banco `ensaio` é apagado; o banco `avalia_sales` não é tocado.
- Restaurar a produção - pare a API e o Worker (a leitura do QR mostra a página de
  espera enquanto isso), restaure pelo contêiner `backup` e suba de novo com a
  `VERSAO` no ar:

  ```bash
  C="docker compose -f docker-compose.producao.yml --env-file /srv/avalia-sales/.env"
  export VERSAO=$(docker inspect --format '{{.Config.Image}}' avalia-sales-api-1 | cut -d: -f2)
  $C stop api worker
  $C exec -T backup pg_restore -d avalia_sales --clean --if-exists --no-owner /backups/<arquivo>.dump
  $C up -d api worker && $C exec nginx nginx -s reload
  ```

## Reservar códigos e importar placas

Os dois comandos servem a qualquer revendedor que já venda placas em outro
sistema, como o Sales da Avalia. Rodam pelo contêiner da API.

Todo comando de manutenção que usa `run` leva `-T` e um `timeout`, como nos
exemplos deste documento: `timeout 600 docker compose ... run --rm -T ...`. O
`run` com terminal anexado, se o terminal fecha ou perde a conexão, pode ficar
girando a CPU do VPS de um núcleo por dias, e a Hostinger pode limitar a CPU
da VPS com uso alto e contínuo. Para achar e encerrar um desses:
`ps -eo pid,etime,pcpu,args --sort=-pcpu | head`, depois
`docker ps -a --filter "name=-run-" -q | xargs -r docker rm -f`.

O arquivo vai em `/srv/avalia-sales/importacao`, que nada cria: crie a pasta e
deixe o CSV legível por todos (`chmod 644`), porque a API roda sem root.

```bash
timeout 600 docker compose -f docker-compose.producao.yml --env-file /srv/avalia-sales/.env run --rm -T \
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
| `comissao_devida_cents` | Comissão de venda paga ainda não repassada; vira saldo do vendedor, que precisa ser da equipe, no mês da importação |
| `total_leituras` | Leituras acumuladas |
| `cliente_email`, `cliente_lugar_no_google` | E-mail e place_id do Google do cliente; o lugar põe o cliente no mapa. Só preenchem campo vazio |

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
  --vendedor <e-mail do sócio 2 no Sales>=<login dele na plataforma> \
  --no-ar-sem-venda-a-zero --so-os-mapeados
```

Ele imprime os totais (placas por situação, valor vendido, vendas não pagas,
comissão devida por vendedor) para conferir com as telas do Sales. As regras
de lá - comissão de 25% sobre o valor, só de venda paga, nunca de sócio; venda
paga é a com `pix_pago_em` ou anterior a 11/10/2026; datas em UTC. Venda sem
placa (a da Lia) fica de fora. Cada vendedor do Sales com comissão devida
precisa de login na equipe antes do `--importar-placas`, com o e-mail do Sales
ou o do `--vendedor`.

O Sales tem placa no ar sem venda, cadastrada antes de 01/10/2026, quando
passou a registrar a venda. O importador recusa placa no ar sem data de venda;
com `--no-ar-sem-venda-a-zero` ela entra como venda de R$ 0 na data do
cadastro, com o dono e sem comissão, e segue lendo sem vencimento. O destino de
toda placa passa como está no Sales.

Com `--comissao-paga`, nenhuma comissão entra como devida: serve quando o
acerto com os vendedores foi feito fora do Sales e o `comissao_paga_em` ficou
vazio.

Com `--so-os-mapeados`, a venda de quem não está no `--vendedor` fica com o
dono. Sem ele, o importador procura o e-mail do Sales entre os logins da
plataforma, e um login que não é da equipe (o da operadora, por exemplo)
ficaria como vendedor.

Para devolver as placas importadas aos lotes em que foram impressas no Sales,
gere também o arquivo de lotes (`converter_do_sales.py ... --lotes lotes.csv`,
colunas `codigo,lote,titulo,categoria,criado_em,arquivar`) e rode
`--lotes-de-origem --revendedor=<CNPJ ou id> --arquivo=...`, primeiro com
`--ensaio`. Cada lote do Sales vira um lote da revenda com o título "Sales
LT-0003 - ..." e a categoria pelo título (teste, crachá, cardápio, campanha é
placa, o resto é avulso); o de teste sai arquivado, com as placas em estoque
baixadas. O lote único da importação, vazio, deixa de existir. Placa que já está
num lote com título é recusada, então não move duas vezes.

Para completar e-mail e lugar no Google dos clientes já importados, rode o
mesmo arquivo com `--completar-clientes` no lugar de `--importar-placas`
(também com `--ensaio` primeiro). Ele acha o cliente pelo código da placa
vendida, só preenche campo vazio e pode rodar de novo. O conversor tira o lugar
do `place_id` do negócio no Sales ou do `placeid=` do link de avaliação; link
`g.page` não traz o lugar.

#### Histórico financeiro das vendas trazidas

A importação das placas traz as vendas sem lançar receita no razão (só a
comissão ainda não repassada, no mês da importação). O `--importar-historico`
leva ao razão a receita e a comissão de cada venda paga no Sales, nos meses de
origem, para a DRE de setembro e outubro mostrar o que aconteceu:

- Receita - o valor da venda, no mês do pagamento (`pix_pago_em`, ou a data da
  venda quando é anterior a 11/10/2026). Venda de R$ 0 não lança nada.
- Comissão devida - a comissão inteira (25%, nunca de sócio), no mês do
  pagamento. A não repassada já lançada pela importação das placas não entra de
  novo; o valor dela precisa ser o mesmo do arquivo.
- Comissão paga - quando o Sales tem `comissao_paga_em`, no mês dele. A devida
  menos a paga deixa o saldo do vendedor em Comissões só com o que falta pagar,
  e o próximo repasse não conta a venda paga lá.
- Sem parte da Avalia: o histórico não leva linha da Avalia.

O arquivo sai do mesmo backup, com as mesmas opções usadas na conversão das
placas (`--socio`, `--vendedor`, `--so-os-mapeados`, `--comissao-paga`,
`--no-ar-sem-venda-a-zero`), mais `--historico` (o `--saida` passa a ser
opcional):

```sh
python3 -I infra/migracao/converter_do_sales.py avalia-AAAA-MM-DD-HHMM.sql.gz --historico historico.csv \
  --socio <e-mail do sócio 1 no Sales> --socio <e-mail do sócio 2 no Sales> \
  --vendedor <e-mail no Sales>=<login na plataforma> ... \
  --no-ar-sem-venda-a-zero --so-os-mapeados
```

Colunas `codigo,pago_em,valor_cents,comissao_cents,comissao_paga_em,vendedor_email`,
datas em UTC, uma linha por venda paga. O conversor imprime por mês de Brasília
as vendas, o valor e a comissão (no mês do pagamento) e a comissão paga (no mês
em que foi paga), e avisa a comissão de vendedor sem `--vendedor`. Com
`--comissao-paga`, a comissão sem `comissao_paga_em` sai como paga na data do
pagamento da venda.

Passo a passo:

1. Converter e conferir os totais por mês com as telas do Sales.
2. Copiar o `historico.csv` para `/srv/avalia-sales/importacao` e ensaiar:
   ```bash
   timeout 600 docker compose -f docker-compose.producao.yml --env-file /srv/avalia-sales/.env run --rm -T \
     -v /srv/avalia-sales/importacao:/importacao api --importar-historico --revendedor=<CNPJ da Avalia> \
     --arquivo=/importacao/historico.csv --ensaio
   ```
   Os totais por mês precisam bater com os do conversor.
3. Valer: o mesmo comando sem `--ensaio`.
4. Conferir a DRE de setembro e a de outubro em Financeiro, e o saldo de cada
   vendedor em Comissões, antes de fechar outubro.

Tudo ou nada, como as outras importações. O arquivo inteiro é recusado quando
uma linha aponta placa sem venda na revenda, venda feita na plataforma, valor
diferente do gravado, comissão diferente da já devida, comissão não paga sem
vendedor da equipe ou vendedor diferente do da venda. Comissão paga de quem não
está na equipe entra na DRE sem vendedor. Mês fechado também recusa o arquivo,
com o mês na mensagem: reabrir o mês em Financeiro e rodar de novo, porque o
razão passaria a linha de mês fechado ao mês corrente. A venda que já tem a
receita do histórico é pulada e contada como já lançada, então o comando pode
rodar de novo sem duplicar.

Conta encerrada não recebe importação, nem em ensaio: a operadora aprova a
reabertura em Revendedores antes.

### Virada do Sales

Feita em 24/10/2026, com os totais conferidos. As placas do Sales estão na
revenda Avalia, e o sistema anterior repassa `avaliaone.com.br/q/{codigo}`
para `sales.avaliaone.com.br/q/{codigo}` com 302 e sem cache. Venda, troca,
renovação, destino e suspensão de placa são feitos só na plataforma.

- O repasse fica para sempre: as placas impressas apontam para
  `avaliaone.com.br/q/`.
- Não há volta para o Sales. O que quebrar se corrige na plataforma, ou com a
  placa regravada (cliente sazonal).
- Próximo passo - desligar o módulo de vendas do sistema anterior, mantendo o
  repasse do `/q/`.

## Resultado do grupo

O financeiro da operadora mostra o resultado consolidado da Avalia empresa: a revenda do grupo
mais a operadora, com as eliminações entre as duas (PDD, seção 7.5; ADR 0025). Dois passos
dependem de quem opera:

- Marcar a revenda do grupo - em Revendedores, "Marcar como do grupo da operadora" na revenda
  Avalia. Sem a marca, o consolidado mostra só a operadora. É decisão explícita, vale para todos
  os meses e fica com quem marcou e quando. Marque só a revenda da mesma empresa da operadora:
  a marca deixa a operadora ler o resultado dela, somado. "Tirar do grupo" desfaz.
- Informar o custo dos lotes - em Resultado do grupo, aba Custo dos lotes, a gráfica e o
  acrílico de cada lote. Lote despachado sem custo aparece como pendência, e a placa vendida dele
  não leva custo ao consolidado até o custo ser informado. Os lotes que já existiam ficam
  pendentes até alguém informar.

Depois da migration, o livro da operadora já traz a parte da Avalia nos serviços (e a taxa da
plataforma de antes de 27/10/2026, somada a ela) e os pedidos ativados de antes, e a conciliação do
mês mostra se os dois lados fecham. O fechamento do mês da operadora é separado do
fechamento das revendas.

## Logs e erros

- `docker compose -f docker-compose.producao.yml --env-file /srv/avalia-sales/.env logs -f api worker`
  (sem o `--env-file`, o Compose recusa qualquer comando pelas variáveis obrigatórias).
- Cada serviço guarda no máximo 5 arquivos de log de 10 MB (`x-registro` no
  compose): o log não enche o disco. A mudança vale para o contêiner recriado
  (`up -d`), e o histórico anterior a 50 MB some.
- Dependências - o job `vulnerabilidades` do CI falha com vulnerabilidade alta
  ou crítica conhecida em pacote NuGet (inclusive transitivo) ou npm de
  produção. Atualização de rotina é rodada manual no `main`, com a
  documentação; versão maior entra numa rodada própria.
- Sentry com `SENTRY_DSN` (API e Worker) e `SENTRY_DSN_DO_FRONT` (front, na
  compilação). Sem dado pessoal: nem corpo, nem cookie, nem IP.
- Alertas do Sentry - monitor de disponibilidade em `/api/health` a cada
  60 s (alerta depois de 3 falhas), configurado no painel do Sentry e fora do
  repositório, e check-in do Worker a cada 5 minutos (`Rotinas.cs`, monitor
  `avalia-sales-worker`, alerta depois de 2 faltas). Worker parado ou API fora
  chegam por e-mail, pela regra de alerta do painel do Sentry, sem ninguém
  olhar o painel.
- Log de acesso do Nginx sem query string (`$uri`, formato `sem_query`): o
  busca e filtros, que levam nome e telefone, não ficam gravados.
- Enquanto a API reinicia, `/q/` e `/l/` mostram "Abrindo o link, só um
  instante." e recarregam em 3 s, no lugar do 502.
- Fila parada - e-mails sem Resend esperam na tabela `emails`; pedido de venda
  sem Protheus não entra em `envios_ao_erp` (a tela ERP da operadora avisa que a
  integração está desligada); o que entrou antes espera o endereço; webhook que
  falhou 10 vezes fica em `eventos_externos` com o último erro.

## Incidente

Incidente é a plataforma fora do ar, cobrança ou leitura de placa parada, ou
suspeita de acesso indevido a dado de cliente.

1. Confirmar - `/api/health` pelo domínio, o painel do Sentry (monitor e
   check-in do Worker) e
   `docker compose -f docker-compose.producao.yml --env-file /srv/avalia-sales/.env ps`.
2. Classificar:
   - Grave - dado pessoal exposto ou alterado, dinheiro cobrado errado, placa
     sem responder em `/q/`.
   - Médio - painel fora com a leitura de placa no ar, fila parada (e-mail,
     webhook).
   - Leve - tela com defeito e contorno.
3. Conter - voltar à versão anterior (seção Voltar), suspender a revenda
   afetada, trocar o segredo exposto (seção Segredos) ou bloquear o domínio
   (Bloqueados).
4. Avisar as revendas afetadas por e-mail, com o que parou, o que fazer e a
   próxima atualização. Grave e médio - aviso em até 1 hora.
5. Dado pessoal - incidente que pode causar risco ou dano relevante aos
   titulares é comunicado à ANPD e aos titulares em até 3 dias úteis do
   conhecimento, com complemento em até 20 dias úteis (Resolução CD/ANPD
   15/2024). Todo incidente com dado pessoal, comunicado ou não, fica
   registrado por 5 anos.
6. Registro - o que houve, a hora de início e de fim, o impacto, a causa e a
   correção, numa linha nova do registro de incidentes (fora do repositório,
   com o mesmo acesso da operadora). Causa que pede mudança vira teste ou
   trava no mesmo trabalho da correção.

### Disco cheio

O contêiner `backup` confere o disco uma vez por dia, depois da cópia das
02:00, e manda e-mail para `BACKUP_PARA` quando ele passa de `DISCO_ALERTA` por
cento (85) e quando a cópia diária falha. O alerta exige `BACKUP_PARA`,
`BACKUP_SENHA` e o Resend: sem qualquer um deles, fica só no log.

```bash
df -h /
docker system df
docker image prune -a --filter "until=168h"   # imagens sem uso com mais de 7 dias, inclusive a da versão anterior
ls -lh /srv/avalia-sales/backups | tail
```

O `prune -a` apaga também a imagem da versão anterior que o Deploy guarda:
depois dele, voltar recompila.

As cópias saem sozinhas depois de `BACKUP_DIAS` dias e os logs dos contêineres
têm teto de 50 MB por serviço. O Postgres para de gravar sem espaço: liberar
antes de 95%.

## Recuperação de conta

- Senha esquecida - a própria pessoa usa "Esqueci a senha" na entrada.
- Segundo fator perdido, com os códigos de recuperação - entra com um código
  de recuperação e liga de novo em Segurança.
- Segundo fator perdido, sem os códigos - a operadora confere a identidade
  fora da plataforma (contato do contrato, documento e o e-mail do login) e
  desliga pela linha de comando, com o motivo:

  ```bash
  timeout 600 docker compose -f docker-compose.producao.yml --env-file /srv/avalia-sales/.env run --rm -T api \
    --desligar-segundo-fator --email=dono@revenda.com.br --motivo="celular perdido, conferido por telefone em 27/10"
  ```

  As sessões são encerradas e o e-mail do login recebe o aviso. A operadora
  que perdeu o próprio segundo fator passa pelo mesmo comando, rodado por quem
  opera o servidor; até lá, as ações de dinheiro da operadora ficam travadas
  (ADR 0027).
- E-mail perdido - `--trocar-email` (seção Primeira instalação), depois de
  conferir a identidade do mesmo jeito.

## Captcha e limites do cadastro

O cadastro público (`/cadastro`) não cria conta que funcione: o dono entra só
depois de abrir o link do e-mail e criar a senha. Cadastro sem confirmação é
descartado pelo Worker em 48 horas, e o e-mail e o CNPJ ficam livres.

**Captcha (Cloudflare Turnstile).** Desligado enquanto `TURNSTILE_CHAVE_SECRETA`
estiver vazia. Para ligar:

1. No painel da Cloudflare, Turnstile, Add widget. Nome: Avalia Sales. Hostname:
   o `DOMINIO` de produção (por exemplo `sales.avaliaone.com.br`). Modo: Managed.
2. Copie a Site Key (pública) e a Secret Key.
3. Grave as duas chaves no ambiente `producao` do GitHub, e o Deploy as repassa
   ao Compose (que lê o ambiente antes do `.env`):
   ```bash
   gh variable set TURNSTILE_CHAVE_SITE --env producao --body '<site key>'
   gh secret set TURNSTILE_CHAVE_SECRETA --env producao
   ```
   Ou, sem o Deploy, no `.env` do VPS (`TURNSTILE_CHAVE_SITE` e
   `TURNSTILE_CHAVE_SECRETA`). Quem sobe o Compose à mão no servidor precisa ter
   as duas no `.env`: sem a secreta, a API recriada sobe com o captcha
   desligado e aceita cadastro sem conferir.
4. A chave do site entra no build do front, então publique de novo (`VERSAO`
   nova, seção "Publicar"); só reiniciar não basta. A secreta é lida pela API
   (`Turnstile__ChaveSecreta`) ao subir.
5. Ligue as duas juntas. Só a secreta: o cadastro recusa, porque a tela não
   manda token. Só a do site: o widget aparece e o servidor não confere.
6. A CSP do Nginx já libera `challenges.cloudflare.com` (script, iframe e
   conexão) na tela do app. Se o widget não aparecer, confira no console do
   navegador se há bloqueio de CSP.

Com o captcha ligado e a Cloudflare fora do ar (ou a secreta errada), o
cadastro responde 503 e não cria nada: falha fechada. O log da API mostra
`Turnstile indisponível ou mal configurado`. Chaves de teste da Cloudflare, que
sempre aprovam: site `1x00000000000000000000AA`, secreta
`1x0000000000000000000000000000000AA`.

**Limites** (todos respondem sem dizer se o e-mail ou o CNPJ já existem):

| Limite | Valor | Configuração | Ao estourar |
| --- | --- | --- | --- |
| Cadastros por IP | 10 por hora | `Limites__CadastrosPorHoraPorIp` | 429 |
| E-mails do cadastro por endereço | 3 por hora, 1 por minuto | `Cadastro__EmailsPorEnderecoPorHora`, `Cadastro__EsperaEmSegundos` | 202 igual, sem e-mail |
| Tentativas por CNPJ | 5 por hora (memória da API) | `Cadastro__TentativasPorCnpjPorHora` | 202 igual, sem e-mail |
| E-mails do cadastro no dia (teto global) | 300 por 24 horas | `CADASTRO_EMAILS_POR_DIA` no `.env` | 503 para todos |
| Validade do cadastro pendente | 48 horas | `Cadastro__PendenteExpiraEmHoras` | Worker descarta |

Só o teto diário vem do `.env`. As outras configurações valem com o padrão do
código: o Compose não as repassa, e mudar uma pede a variável em
`x-integracoes` no `docker-compose.producao.yml`.

O teto diário conta a tabela `emails` (chave `cadastro:`), então reiniciar a API
não o zera. Quando ele é atingido, a API registra o aviso `teto diário ... atingido`
(sem dado pessoal); veja com
`docker compose -f docker-compose.producao.yml --env-file /srv/avalia-sales/.env logs api | grep teto`.
As consultas abaixo rodam no banco com
`docker compose -f docker-compose.producao.yml --env-file /srv/avalia-sales/.env exec -T postgres psql -U avalia_dono -d avalia_sales -c "<consulta>"`.
Conferir o uso do dia:

```sql
select count(*) from emails where chave like 'cadastro:%' and criado_em > now() - interval '24 hours';
```

Cadastros pendentes agora (login criado e ainda sem confirmação):

```sql
select count(*) from usuarios u where not email_confirmed and papel = 'Dono'
  and exists (select 1 from links_de_senha l where l.usuario_id = u.id and l.finalidade = 'CadastroPendente');
```

Dono que já existia antes dessa regra e nunca confirmou o e-mail não é
pendente: continua entrando e confirma pelo painel, como antes.

## Segredos

No `.env` do VPS, fora do repositório; a chave secreta do Turnstile fica no
ambiente `producao` do GitHub (seção Captcha). Toda troca no `.env` vale
depois de recriar API e Worker como na seção Pagar.me em produção, passo 2.

- `JWT_CHAVE` - a troca derruba os tokens de acesso (15 minutos) e nada mais:
  o refresh fica no banco.
- Segredo do webhook do Pagar.me - a troca pede o endereço novo no painel do
  Pagar.me.
- Chaves de integração (Pagar.me, D4Sign, Resend, Google, bucket) - troque no
  painel do fornecedor e depois no `.env`.
- Senha do Postgres - o `.env` só vale na criação do volume. Troque primeiro
  no banco
  (`docker compose ... exec -T postgres psql -U avalia_dono -d avalia_sales -c "ALTER ROLE avalia_app PASSWORD '<nova>'"`,
  ou `avalia_dono` para a do dono), depois no `.env`, e recrie API e Worker.
- `BACKUP_SENHA` - a troca vale para as cópias novas; guarde a anterior
  enquanto houver cópia cifrada com ela no bucket (30 dias) ou no e-mail.

### Chave dos e-mails sensíveis

`CHAVE_DOS_EMAILS` (variável `Seguranca__ChaveDosEmails` na API e no Worker)
cifra o assunto e o texto dos e-mails com link de senha ou código de acesso
enquanto esperam na fila (AES-256-GCM, nonce aleatório por linha). Tem de ser a
mesma nos dois contêineres e ter 32 bytes em base64. Sem ela, a API e o Worker
não sobem (o Compose também recusa o `up`). Só em Development e nos testes
vale uma chave embutida.

Gerar:

```bash
openssl rand -base64 32
```

Rotação, sem perder e-mail na fila:

1. No `.env`, `CHAVE_ANTERIOR_DOS_EMAILS` recebe o valor atual e
   `CHAVE_DOS_EMAILS` recebe a chave nova.
2. Suba a API e o Worker juntos, com a `VERSAO` no ar e o reload do Nginx
   (seção Pagar.me em produção, passo 2). O que já estava cifrado ainda abre
   pela anterior; o que entra agora usa a nova.
3. Depois de 2 dias (a maior validade de link ou código) esvazie
   `CHAVE_ANTERIOR_DOS_EMAILS` e suba de novo.

Se o Worker não conseguir abrir um e-mail (chave diferente da API, ou chave
perdida), o e-mail vai para falha definitiva com o conteúdo apagado e o log
registra só o id; o usuário pede o link ou o código de novo. Conferir:

```sql
select count(*) from emails where ultimo_erro like 'Conteúdo cifrado%';
```

## Ações das mudanças de 26/10/2026

- CNPJ da revenda Avalia - a operadora corrige o nome e o CNPJ em Revendedores (editar a revenda). O contrato já assinado da revenda Avalia segue com o documento de antes: registre um contrato novo se o documento mudou de verdade.
- Segundo fator da operadora - ligue em Segurança antes de trocar recebedor, CNPJ, registrar pagamento por fora, estornar pedido ou lançar acerto. A exigência vem ligada (`Plataforma__ExigirSegundoFatorDaOperadora`, padrão `true`); desligar só no desenvolvimento local.
- Recebedor próprio da revenda Avalia - falta criar no painel do Pagar.me e informar em Revendedores (decisão de 26/10).
- Migrations novas (`LimiteDeDestinos`, `FornecedoresEEntrega`, `AcertosDeRevenda`, `AvisoDeVencimentoDaPlaca`, `EstornoELembretesDoPedido`, `DestinosBloqueados`) são só aditivas; a publicação roda `--migrar` como de costume.
- Destinos bloqueados - Bloqueados no menu da operadora. Bloquear vale na hora para as placas e links que já apontam para o domínio. Desbloquear devolve o redirecionamento.
- Avisos de vencimento e lembretes de ativação - saem pelo Worker, sem configuração. Os e-mails ao cliente usam o remetente da revenda e só saem se o cliente tem e-mail.

## Ações das mudanças de 27/10/2026

- Revenda receptora - a migration `RevendaReceptora` marca como receptora a revenda de CNPJ 39914870000101. Confira em Revendedores o selo "Receptora" na revenda Avalia; se estiver em outra ou em nenhuma, use "Marcar como receptora" na linha certa (pede o segundo fator). Sem receptora, nenhuma revenda consegue pedir o encerramento, e um distrato assinado espera a marca em `eventos_externos` com "Nenhuma revenda receptora".
- `Plataforma__CnpjDaRevendaDaAvalia` saiu: apague a variável do `.env` e do ambiente `producao` do GitHub, se estiver lá.
- ERP - sem `Protheus__UrlBase`, pedido pago não entra mais em `envios_ao_erp`. As linhas que já estavam lá ficam como estão; com o endereço configurado, o Worker as envia.
