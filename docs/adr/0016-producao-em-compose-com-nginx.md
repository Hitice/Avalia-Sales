# 0016. Produção em Docker Compose com Nginx na frente

- Situação - Aceita
- Data - 21/10/2026

## Contexto

A plataforma roda num VPS Linux de um núcleo e 4 GB, com Docker, Nginx, Let's
Encrypt e GitHub Actions com runner próprio no servidor. `/q/{codigo}` está
impresso em placas: não pode cair na publicação nem ficar preso numa versão
defeituosa.

## Opções

- Kubernetes
- Docker Compose no VPS, com Nginx servindo o front e fazendo o proxy

## Decisão

Compose (`docker-compose.producao.yml`) com Nginx (front compilado e proxy),
API, Worker, `migrar`, Postgres, Gotenberg (ADR 0018), certbot e backup. Só o
Nginx publica porta. As imagens levam o commit como versão (`VERSAO`) e são
construídas no próprio VPS, uma por vez.

O deploy (`.github/workflows/deploy.yml`) roda no runner do VPS depois do CI
verde num push ao `main` e da aprovação no ambiente `producao` do GitHub.
Voltar é publicar o commit anterior pelo mesmo workflow. O `migrar` roda antes
da API, e migration só acrescenta (`.claude/rules/migrations.md`). Enquanto o
VPS não existe, o deploy fica desligado pela variável `DEPLOY_ATIVO`.

## Consequências

- Uma máquina e poucos serviços, sem orquestrador para manter.
- Voltar de versão não desfaz banco: a versão anterior roda sobre o banco com
  colunas a mais. Isso depende de toda migration ser aditiva. O hook
  `migration-aditiva.sh` impede editar migration já commitada, mas nada no CI
  recusa uma migration nova que apague ou renomeie coluna.
- A máquina é ponto único. O contêiner `backup` copia o banco às 02:00 de
  Brasília para o disco e, se configurado, para e-mail cifrado e bucket S3. Com
  `BACKUP_PARA`, avisa por e-mail a cópia que falha e o disco acima de
  `DISCO_ALERTA` (85%). O log de cada contêiner tem teto (`x-registro` - 10 MB
  por arquivo, 5 arquivos).
- Crescer é aumentar a máquina. Mais réplicas da API atrás do Nginx pedem antes
  levar ao banco os limites de tentativa, que hoje ficam na memória de cada
  processo (limitador de taxa do ASP.NET e `JanelaDeTentativas`). O hub de
  avisos já funciona com várias instâncias (ADR 0013).
