# 0016. Produção em Docker Compose com Nginx na frente

- Situação - Aceita
- Data - 21/10/2026

## Contexto

A plataforma roda em servidor próprio (Linux, Docker, Nginx, Let's Encrypt,
GitHub Actions com runner próprio), e `/q/{codigo}` está impresso em placas:
não pode cair na publicação nem ficar preso numa versão ruim.

## Opções

- Kubernetes
- Docker Compose num VPS, com Nginx servindo o front e fazendo o proxy

## Decisão

Compose com Nginx (front compilado e proxy), API, Worker, `migrar`, Postgres,
certbot e backup. Imagens marcadas pelo commit; o deploy roda no runner do
VPS depois do CI verde e com aprovação; a volta é publicar o commit anterior.
Migration só aditiva.

## Consequências

- Uma máquina, poucos serviços, nenhum orquestrador para manter.
- A volta não precisa desfazer banco.
- Escalar a API é subir mais réplicas atrás do mesmo Nginx; o hub de avisos já
  funciona com várias instâncias (ADR 0013).
