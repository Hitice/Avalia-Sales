# 0019. Transferência dos clientes no encerramento por uma função e uma política próprias

- Situação - Aceita, destino substituído pelo ADR 0029
- Data - 22/10/2026
- Revista - 27/10/2026, o destino deixou de ser a revenda do CNPJ em
  `Plataforma:CnpjDaRevendaDaAvalia` e passou a ser a revenda marcada como
  receptora (ADR 0029). A função e as políticas abaixo continuam valendo

## Contexto

O distrato passa os clientes ativos da revenda que encerra para outra revenda
(cláusula 2). Isso muda o `revendedor_id` de placas, clientes e links. O RLS
(ADR 0001) existe para uma linha nunca sair da revenda da sessão: a política
de isolamento confere a linha nova e a antiga contra o mesmo revendedor.

## Opções

- Copiar as linhas para a revenda nova e apagar as antigas
- Uma função no banco com permissão de dono (`SECURITY DEFINER`)
- Uma política de RLS só para a transferência, ligada por uma variável da transação

## Decisão

A função `transferir_clientes_da_revenda(origem, destino, momento)` (migration
`EncerramentoDeConta`), chamada por `EfeitosDoEncerramento` pela
`TransferenciaDeClientes`. Ela recusa se a sessão não for a da revenda de
origem ou se origem e destino forem a mesma. Liga `app.transferencia` só dentro
da própria transação e muda, de uma vez:

- as placas no ar - ativas ou suspensas, dentro da carência de 30 dias depois
  do vencimento;
- os clientes dessas placas e os links curtos desses clientes;
- `placas_emitidas` e `codigos_de_link`, que a leitura do QR e do link curto
  usa para achar a revenda.

A renovação em aberto dessas placas é cancelada na revenda antiga. As
políticas `transferencia_no_encerramento` (UPDATE) e
`transferencia_no_encerramento_leitura` (SELECT, porque o Postgres confere a
linha nova também contra o SELECT) valem só com a variável ligada. A venda fica
na revenda antiga; a placa guarda o cliente e o valor da venda para a
renovação (`cliente_da_transferencia_id`, `valor_da_venda_cents`).

## Consequências

- `/q/{codigo}` continua respondendo depois da transferência.
- A revenda antiga deixa de ver os clientes transferidos, como diz a cláusula 3.2.
- Copiar e apagar quebraria as chaves de vendas, leituras e renovações. A
  função com permissão de dono só escaparia do RLS forçado se o dono do banco
  fosse superusuário, e o isolamento não deve depender disso.
- A variável é ligada pela aplicação, com a mesma confiança de
  `app.revendedor_id`: o isolamento protege contra filtro esquecido, não contra
  código que decide ligar a variável. Só a função liga.
- A carência de 30 dias está escrita na função. Mudar `Placa.CarenciaDias` pede
  migration nova que recrie a função.
