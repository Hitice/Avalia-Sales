# 0029. Revenda receptora no encerramento por marca explícita

- Situação - Aceita
- Data - 27/10/2026
- Substitui - a escolha do destino da transferência no ADR 0019 (a revenda do
  CNPJ em `Plataforma:CnpjDaRevendaDaAvalia`); a função e as políticas da
  transferência continuam as do ADR 0019

## Contexto

O distrato passa os clientes ativos de quem encerra para a revenda que os
assume (cláusula 2). O destino era a revenda cujo CNPJ batia com a opção
`Plataforma:CnpjDaRevendaDaAvalia`. A opção e o cadastro são mudados em lugares
diferentes: com o CNPJ da revenda corrigido em Revendedores e a variável do
ambiente esquecida, o encerramento seguia sem transferir e só avisava no sino.
Além disso, a regra perguntava qual revenda é a Avalia, o que o produto não faz.

## Opções

- Manter a opção de CNPJ e conferir na subida da API que existe revenda com ele
- Marca na revenda, dada pela operadora, no máximo uma na plataforma
- Escolher o destino a cada encerramento, na hora da assinatura

## Decisão

A segunda. `revendedores.receptora_no_encerramento` (bool, padrão falso) com
índice único parcial `WHERE receptora_no_encerramento`. A operadora marca em
`PUT /operadora/revendedores/{id}/receptora`. Marcar uma tira a marca da
anterior na mesma transação, com trava no Postgres (`TravarReceptora`,
`pg_advisory_xact_lock`) para duas trocas juntas. A rota exige o segundo fator
(ADR 0027), porque a marca decide para onde vão os clientes e as renovações
deles, como o CNPJ decidia, e a troca de CNPJ já pede o segundo fator. A
migration `RevendaReceptora` marca a revenda de CNPJ 39914870000101, se não
estiver encerrada, para a produção seguir igual.

- Receptora ativa - marcada, não encerrada e diferente da revenda que encerra
  (`EfeitosDoEncerramento.Receptora`).
- Sem receptora ativa, `Encerramento.Pedir` recusa com "Encerramento
  indisponível no momento. Abra um chamado no Suporte." e nenhum distrato é
  gerado.
- `EfeitosDoEncerramento` recusa antes de qualquer efeito com "Nenhuma revenda
  receptora. Marque uma em Revendedores.": o evento do distrato assinado volta
  para a fila com esse erro e encerra quando houver receptora.
- A receptora não pede o próprio encerramento; revenda encerrada ou com
  encerramento pedido não vira receptora; tirar a marca com encerramento em
  andamento é recusado.

## Consequências

- Some a divergência entre configuração e cadastro: trocar o CNPJ da revenda
  não muda o destino dos clientes.
- Encerramento sem destino deixa de acontecer em silêncio; o dono vê a recusa
  e a operadora vê o erro no evento.
- A primeira opção ainda dependeria de duas fontes iguais; a terceira pediria
  decisão da operadora em todo distrato, com o documento já assinado.
- A receptora sair da plataforma pede, antes, outra receptora marcada.
