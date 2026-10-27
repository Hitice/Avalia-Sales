# 0025. Livro da operadora e leitura restrita das revendas do grupo

- Situação - Aceita
- Data - 26/10/2026
- Complementa - 0008, 0011, 0015, 0020
- Revista - 27/10/2026, a taxa da plataforma saiu (ADR 0003). O livro guarda a parte da Avalia
  nos serviços e o pedido de placas; o tipo `TaxaDaPlataforma` fica só para as linhas antigas e
  os estornos delas

## Contexto

A Avalia empresa é a revenda Avalia mais a operadora. O dono precisa do resultado
consolidado dela, e a revisão contábil fixou o desenho: consolidação com eliminação das
transações internas (CPC 36 / IFRS 10), receita reconhecida quando o controle passa ao
cliente (CPC 47 / IFRS 15) e informação por segmento (CPC 22 / IFRS 8). O preço do pacote da
revenda Avalia é o mesmo das outras revendas.

O que a operadora ganha de cada revenda (a parte da Avalia nos serviços, os pedidos de placas e,
até 27/10/2026, a taxa da plataforma) não tem onde ficar: o razão é de revendedor, e a operadora não tem
revendedor (ADR 0008). Para consolidar, a operadora também precisa ler o DRE da revenda do
mesmo grupo, sem abrir o das outras.

## Opções

Para o que a operadora ganha:

- Lançar na revenda Avalia - obriga a regra a perguntar quem é a Avalia, e mistura o papel de
  operadora com o de revenda.
- Livro próprio da operadora, só de inserção - separa os dois papéis.

Para ler o DRE da revenda do grupo:

- Abrir `lancamentos`, `vendas` e `cobrancas` à operadora, por política de RLS, nas revendas
  marcadas - a operadora passaria a ler linha a linha, em toda consulta, e uma consulta mal
  escrita vazaria a revenda.
- Funções `SECURITY DEFINER` que devolvem os agregados - duplica em SQL a apropriação em doze
  partes e o DRE, que já estão no `Dre` e no `Reconhecimento`.
- Reaproveitar o `Dre` numa sessão da revenda marcada, aberta por uma leitura restrita que
  devolve só números somados.
- Marcar a revenda por CNPJ fixo ou por "é a Avalia" - descartada: o grupo é decisão da
  operadora, e nenhuma regra pergunta quem é a Avalia.

## Decisão

Livro próprio, `lancamentos_da_operadora`, só de inserção (`REVOKE UPDATE, DELETE` do papel da
aplicação), com `revendedor_origem_id` (de quem veio o ganho), tipo, valor com sinal, origem,
venda, competência em Brasília, `por_fora` (parte "a acertar") e `estorna_id`. Os índices únicos
são os do razão: o mesmo fato não é lançado duas vezes, e a mesma linha não é estornada duas vezes.

- A parte da Avalia nos serviços é lançada no mesmo ponto em que o razão da revenda lança
  `TaxaAvalia` (`EfeitosDaCobranca`), com o mesmo Id da linha do razão, e o estorno do razão
  estorna a linha do livro no mesmo `SaveChanges`. O Id comum é o que deixa a revenda estornar sem ler
  o livro. A parte de recebido por fora entra marcada. Até 27/10/2026 a taxa da plataforma era
  lançada do mesmo jeito, com o tipo `TaxaDaPlataforma`; o tipo da linha do livro é escolhido pelo
  começo da descrição da linha do razão ("Parte da Avalia"), em `LivroDaOperadora`.
- O pedido de placas é receita da operadora na ativação do lote (`PedidosDePlacas.Ativar`), e não no
  pagamento. Antes disso é adiantamento de cliente, mostrado como "recebido e ainda não reconhecido".
- Despesas da operadora (CRUD) e o fechamento do mês dela seguem o ADR 0020: editar estorna e lança de
  novo, excluir só estorna, e o gatilho `lancamento_da_operadora_em_mes_aberto` passa ao mês corrente a
  linha de mês fechado.
- O custo real de produção é cadastro da operadora por lote (`custos_de_lote`: gráfica e acrílico), com
  criar, editar e excluir. O custo por placa é o total dividido pela quantidade, arredondado para baixo.

Política de RLS:

| Tabela | Leitura | Escrita |
| --- | --- | --- |
| `lancamentos_da_operadora` | Só a operadora | Operadora; ou a revenda, só com ela mesma como origem (`revendedor_origem_id`); nunca UPDATE nem DELETE |
| `despesas_da_operadora`, `custos_de_lote` | Só a operadora | Só a operadora |
| `fechamentos_da_operadora` | Qualquer sessão (o gatilho do livro lê o mês fechado na sessão de quem lança) | Só a operadora |

A operadora continua sem ler as tabelas das revendas, salvo as marcadas `IAtendidoPelaOperadora`
(ADR 0015). Nenhum papel ganha `BYPASSRLS`.

A revenda do grupo é marca em `revendedores.do_grupo_da_operadora`, dada pela operadora por
`PUT /operadora/revendedores/{id}/grupo`, com quem marcou e quando (`grupo_definido_por`,
`grupo_definido_em`) e a rota na trilha da operadora. Para o consolidado, `ResultadoDoGrupo` lê cada
revenda marcada pelo `IEscopoDeRevenda`: abre uma sessão do banco com a revenda na sessão do Postgres
(o mesmo mecanismo do dono, com filtro do EF e RLS valendo), roda o `Dre` e as consultas da apropriação, e
devolve só números somados. A implementação confere a marca no banco a cada leitura e recusa a revenda não
marcada, mesmo com o id certo, e o escopo só serve a quem entra como operadora.

## Consequências

- Nenhuma regra pergunta se a revenda é a Avalia. A revenda Avalia é marcada como qualquer outra.
- Risco aceito - a operadora, ao marcar uma revenda, autoriza a leitura agregada do DRE dela. Marcar revenda
  de terceiro seria expor o resultado dela; por isso a marca é decisão explícita, pede confirmação na tela e
  fica na trilha da operadora. Ela não pede o segundo fator (ADR 0027). Dentro do escopo, o código da leitura vê a sessão inteira da revenda marcada;
  o que impede o vazamento é a leitura só devolver agregados, o que o teste cobre.
- Revenda fora do grupo aparece só pelo que paga à operadora; o DRE dela nunca é lido, e o teste prova isso
  mesmo com o id dela.
- O consolidado é calculado, e não lançado. O livro fecha por mês; a linha de custo real das placas
  vendidas é lida do custo vigente do lote, então corrigir o custo de um lote muda também meses já fechados.
  O fechamento congela o livro, e não essa linha.
- O custo real da placa vendida acompanha a receita da venda, nas mesmas doze partes do `Reconhecimento`,
  para o custo e a receita ficarem no mesmo período. Se o dono preferir o custo inteiro no mês da venda, a
  troca é local a `ResultadoDoGrupo.ApurarCustoReal`.
- Se um lado da eliminação estiver em mês fechado e o outro não, o gatilho move uma das linhas ao mês corrente
  e a conciliação mostra a diferença do mês. A diferença some quando o mês é reaberto ou fechado nos dois lados.
- A tarifa do gateway não aparece no consolidado porque o razão não a guarda (ela sai no extrato do Pagar.me).
- O backfill da migration lança no livro as taxas já lançadas no razão e os pedidos já ativados, sem tocar
  nas linhas existentes, e pode rodar de novo sem duplicar (`BackfillDoLivroDaOperadora`).
