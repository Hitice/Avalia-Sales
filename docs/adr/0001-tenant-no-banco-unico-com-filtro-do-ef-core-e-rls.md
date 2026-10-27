# 0001. Tenant no banco único com filtro do EF Core e RLS

Cada revendedor é um tenant: todos usam a mesma plataforma e o mesmo banco, e
cada um só acessa os próprios dados. Duas travas independentes garantem isso:
o filtro do EF Core nas consultas da aplicação e o RLS (Row Level Security, a
política por linha do PostgreSQL) no banco. Se uma falhar, a outra segura.

- Situação - Aceita
- Data - 21/10/2026

## Contexto

A plataforma atende vários revendedores no mesmo banco. Dado de um revendedor
aparecer para outro é o pior defeito possível do produto.

## Opções

- Banco por revendedor - uma migration e um pool de conexões por revendedor,
  num servidor só
- Esquema por revendedor - a mesma migration repetida em cada esquema, e a
  consulta da operadora entre revendedores fica difícil
- Banco único com `revendedor_id`, filtro global do EF Core e RLS

## Decisão

Banco único com duas travas independentes:

- Filtro do EF Core - montado por expressão para toda entidade `IDoRevendedor`
  (`BancoDeDados.FiltrarPorRevendedor`): só passa a linha do revendedor da
  requisição. Tabela nova ganha o filtro sozinha.
- RLS - `ENABLE` e `FORCE ROW LEVEL SECURITY` com a política
  `isolamento_por_revendedor`, que lê `app.revendedor_id`, gravado em cada
  conexão aberta (`RevendedorNaConexao`). Sem revendedor, o valor fica vazio e
  nenhuma linha passa.

O revendedor de um registro novo sai sempre da requisição, nunca do corpo do
pedido, e não muda depois de gravado. A aplicação conecta com o papel
`avalia_app`, sem superusuário e sem `BYPASSRLS`; quem migra é o dono do banco.

## Consequências

- Uma migration só para todos os revendedores.
- A operadora não tem revendedor e só vê as tabelas marcadas para ela (ADR
  0008 e 0015). A leitura de uma revenda do grupo da operadora abre um contexto
  próprio, com as duas travas (ADR 0025).
- Cada trava tem teste próprio (`IsolamentoEntreRevendedoresTest`): o do
  filtro roda numa conexão que ignora o RLS, e o do RLS roda SQL direto, sem o
  filtro. `PermissoesDoBancoTest` confere que `avalia_app` não é superusuário
  nem ignora o RLS.
- Tabela nova com `revendedor_id` precisa de `IsolarPorRevendedor` na
  migration; o teste `Toda_tabela_com_revendedor_tem_rls_ligado_e_forcado`
  falha sem ele. As exceções ficam listadas no teste, cada uma com o motivo:
  `usuarios`, `vinculos_externos`, `lotes`, `placas_emitidas`,
  `codigos_de_link` e `envios_ao_erp`.
- Tabela da plataforma sem `revendedor_id` (como `revendedores`, `pacotes`,
  `cupons`, `produtos_da_plataforma` e `eventos_externos`) não tem RLS: quem a
  protege é a autorização da rota.
