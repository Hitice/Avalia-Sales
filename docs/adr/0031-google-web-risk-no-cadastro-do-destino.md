# 0031. Google Web Risk no cadastro do destino

- Situação - Aceita
- Data - 27/10/2026

## Contexto

A placa e o link curto redirecionam no domínio da plataforma. Um destino de
phishing ou malware cadastrado por uma revenda usa a reputação desse domínio. A
lista de domínios bloqueados (ADR 0026) só pega o que a operadora já conhece.

## Opções

- Lookup API do Google Web Risk (`uris:search`) a cada destino cadastrado
- Update API do Web Risk, com a lista local de prefixos de hash
- Google Safe Browsing API
- Só a lista da operadora

## Decisão

A primeira. Ao cadastrar ou trocar destino de placa ou de link,
`DestinosBloqueados.ExigirPermitido` consulta, depois da lista da operadora,
`IReputacaoDeEnderecos`. O `ClienteWebRisk` chama
`GET https://webrisk.googleapis.com/v1/uris:search` com `threatTypes` MALWARE,
SOCIAL_ENGINEERING e UNWANTED_SOFTWARE e o endereço em `uri`. A chave vai no
cabeçalho `X-Goog-Api-Key`, e não na URL. Resposta `{}` aprova; resposta com
`threat.threatTypes` recusa com "Endereço marcado como perigoso pelo Google.
Use outro.". Conferido na documentação do Google em 27/10/2026.

- Sem `WebRisk:ChaveApi`, a consulta fica desligada.
- Falha aberta: erro, tempo esgotado (3 segundos) ou resposta estranha aprovam
  e vão ao log. A venda não para pelo Google, e a lista da operadora continua
  valendo.
- A consulta é feita antes da transação e da trava dos destinos da revenda,
  para a espera da rede não segurar a trava. Antes dela, o limite do dia é
  conferido sem a trava (`LimiteDeDestinos.ConferirAntes`): o pedido que o
  limite recusa não chega ao Google. A conferência que vale continua na
  transação.
- Na venda de várias placas, uma consulta por endereço distinto, em paralelo:
  com o Google fora do ar, a espera é de 3 segundos, e não 3 por placa.
- A resposta fica guardada na memória da API por endereço: a ameaça até o
  `expireTime` (no máximo 24 horas), o endereço limpo por 10 minutos. O mesmo
  pedido repetido não volta ao Google.
- Só o cadastro consulta. A leitura do QR e o clique no link não, porque a
  leitura é anônima e de volume alto.

A Update API foi descartada por pedir uma lista local sincronizada, para um
volume de consulta que o limite de 10 destinos por dia por revenda deixa baixo.
A Safe Browsing API é só para uso não comercial; o Web Risk é a versão
comercial do mesmo serviço.

## Consequências

- Até 100 mil consultas por mês sem custo; acima disso, US$ 0,50 por mil
  (preço do Google em 27/10/2026). Com o limite conferido antes e a resposta
  guardada, cada revenda consulta por dia perto do seu limite de destinos, mais
  os endereços recusados pelo Google, que ficam guardados. As rotas de
  cadastro não têm limite de requisições próprio.
- O endereço do destino vai ao Google. É link de empresa, sem dado pessoal do
  cliente.
- Destino que vira perigoso depois do cadastro não é pego pela consulta. Ele
  continua dependendo da lista da operadora.
