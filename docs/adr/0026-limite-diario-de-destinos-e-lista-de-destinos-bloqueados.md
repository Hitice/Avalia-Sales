# 0026. Limite diário de destinos e lista de destinos bloqueados

- Situação - Aceita
- Data - 26/10/2026

## Contexto

O QR da placa e o link curto respondem no domínio da Avalia e aceitam como destino
qualquer endereço http ou https que a revenda cadastrar. Um destino malicioso sob
um domínio conhecido da Avalia herda a confiança dele (phishing por QR Code) e só
sairia do ar se a revenda dona o suspendesse. A operadora não escreve em placa nem
em link de revenda (ADR 0015).

## Opções

- Só limitar o cadastro de destinos por revenda e por dia
- Só uma lista de domínios bloqueados, consultada ao cadastrar
- As duas, com a lista também consultada na leitura do QR e no clique do link

## Decisão

A terceira.

- Limite - cada revenda cadastra até 10 destinos novos por dia (Brasília): placa
  vendida com destino, destino trocado, link curto criado ou alterado. O uso do dia
  é contado na auditoria da revenda (ações `DestinoDefinido`, `DestinoTrocado`,
  `LinkCriado`, `LinkAlterado`), sem tabela nova. A revenda pede liberação num
  chamado, e a operadora soma mais cadastros ao limite de hoje
  (`revendedores.destinos_liberados` e `destinos_liberados_em`).
- Lista - `destinos_bloqueados` guarda domínios em minúsculas e sem "www.", vale
  também para subdomínios, tem leitura aberta (a leitura do QR roda na sessão da
  revenda dona) e escrita só da operadora, com criar e excluir. Quem cria destino
  recusa domínio bloqueado. A leitura do QR e o clique do link consultam a lista
  antes de redirecionar e mostram a página "Endereço bloqueado".

## Consequências

- O bloqueio vale na hora para tudo o que já aponta para o domínio, sem a operadora
  escrever em tabela de revenda.
- A lista é reativa: o domínio só entra depois que alguém o denuncia, e um endereço
  que redireciona para outro (encurtador, redirecionamento aberto) passa por ela.
- Cada leitura e cada clique ganham uma consulta à lista (tabela pequena, índice
  único); se o volume crescer, a lista vai para memória com validade curta.
- O limite conta por auditoria, então a contagem acompanha o relógio de Brasília e
  não precisa de rotina para zerar. Desde 27/10/2026 a conferência roda dentro da transação que grava o destino, sob a trava
  de aplicação dos destinos da revenda (a mesma dos links): gravações simultâneas contam uma depois
  da outra, e o limite é garantia.
