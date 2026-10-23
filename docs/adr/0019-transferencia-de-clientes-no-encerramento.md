# 0019. Transferência dos clientes no encerramento por uma função e uma política próprias

- Situação - Aceita
- Data - 22/10/2026

## Contexto

O distrato passa os clientes ativos da revenda que encerra para a revenda da
Avalia (cláusula 2). Isso muda o `revendedor_id` de placas, clientes e links,
e o RLS (ADR 0001) existe justamente para uma linha nunca sair da revenda da
sessão: a política de isolamento confere a linha nova e a antiga contra o
mesmo revendedor.

## Opções

- Copiar as linhas para a revenda nova e apagar as antigas
- Uma função no banco com permissão de dono (`SECURITY DEFINER`)
- Uma política de RLS só para a transferência, ligada por uma variável da transação

## Decisão

A função `transferir_clientes_da_revenda(origem, destino, momento)` (migration
EncerramentoDeConta) roda na sessão da revenda de origem, liga
`app.transferencia` só dentro da própria transação e muda, de uma vez, as
placas no ar (dentro da carência), os clientes delas, os links curtos desses
clientes, `placas_emitidas` e `codigos_de_link`, que a leitura do QR e do link
usa para achar a revenda. As políticas `transferencia_no_encerramento` (UPDATE)
e `transferencia_no_encerramento_leitura` (SELECT, porque o Postgres confere a
linha nova também contra o SELECT) valem só com essa variável. A venda fica na
revenda antiga; a placa guarda o cliente e o valor da venda para a renovação
(`cliente_da_transferencia_id`, `valor_da_venda_cents`).

## Consequências

- `/q/{codigo}` continua respondendo depois da transferência.
- A revenda antiga deixa de ver os clientes transferidos, como diz a cláusula 3.2.
- Copiar e apagar quebraria as chaves de vendas, leituras e renovações; função
  com permissão de dono dependeria de o dono do banco ser superusuário, o que
  o RLS forçado não garante.
- A variável é ligada pela aplicação, com a mesma confiança de `app.revendedor_id`:
  o isolamento continua protegendo contra filtro esquecido, não contra código
  que decide ligar a variável. Só a função liga.
