---
name: padroes
description: Padrões de construção do Avalia Sales (.NET em camadas, EF Core, Postgres com RLS). Consultar antes de criar entidade, tabela, caso de uso, rota, teste ou de adicionar biblioteca, para o código novo sair igual ao que existe e só com bibliotecas consolidadas.
---

# Padrões do Avalia Sales

As regras gerais (tenant, dinheiro, razão, webhook, documentação) estão no
`CLAUDE.md`. Aqui fica o como fazer.

## Onde cada coisa mora

| Projeto | Responsabilidade | Não pode |
| --- | --- | --- |
| `AvaliaSales.Domain` | Entidade, regra, `RegraDeNegocioException` | Depender de qualquer outro projeto ou pacote |
| `AvaliaSales.Application` | Caso de uso, DTO, interface do que precisa (`IBancoDeDados`, `IRevendedorAtual`, `IUsuarioAtual`) | Conhecer HTTP, Npgsql ou Identity |
| `AvaliaSales.Infrastructure` | `BancoDeDados`, migrations, Identity, clientes HTTP de integração, caixa de entrada dos webhooks | Ter regra de negócio |
| `AvaliaSales.Api` | Rota, autenticação, limite de requisições, idempotência, tradução de erro em status | Ter regra de negócio ou SQL |
| `AvaliaSales.Worker` | Fila de eventos externos, retomadas, e-mail, ERP, PDF | Ter rota HTTP |

A coluna "Não pode" é regra de revisão: nenhum teste confere as referências
entre projetos.

Regra recusada sai do Domain como `RegraDeNegocioException`, com a mensagem
escrita para a tela, e vira 422 (`RegraDeNegocioEm422`). Erro de programação é
exceção comum e vira 500 sem pilha. Formato de entrada (tamanho, obrigatório,
faixa) é conferido na borda, com DataAnnotations nas opções de configuração e
checagem explícita no caso de uso; a tela nunca é a única validação.

## Dinheiro

- Centavos em `long` (`bigint` no banco), coluna terminada em `_cents`.
- Taxa e percentual em pontos base, `int`, coluna `_bps` (1,99% = 199).
- Nada de `decimal`, `double` ou `float` em dinheiro, em nenhuma camada.
- Preço, comissão e taxa são copiados para a venda no momento dela. Mudança de
  tabela não altera venda feita.

## Tabela nova do revendedor

1. A entidade implementa `IDoRevendedor`. O filtro global do EF Core vem sozinho.
2. Configurar em `BancoDeDados.OnModelCreating`: `ToTable` em snake_case
   plural, FK para `revendedores` com `DeleteBehavior.Restrict`, índice que
   começa por `RevendedorId`.
3. Gerar a migration:
   ```bash
   dotnet ef migrations add <Nome> -p src/AvaliaSales.Infrastructure -s src/AvaliaSales.Infrastructure -o Banco/Migrations
   ```
4. No fim do `Up`, escrever à mão `migrationBuilder.IsolarPorRevendedor("<tabela>");`.
   O teste `Toda_tabela_com_revendedor_tem_rls_ligado_e_forcado` falha sem ela.
5. Teste de isolamento pela API: o que o revendedor A grava não aparece para o B.

Tabela da operadora (sem revendedor) segue o ADR 0008 e o ADR 0015: marcada
como da plataforma, e a operadora grava só nas que atende.

Migration só aditiva: coluna nova nasce nula ou com padrão, e remoção vem numa
migration posterior, depois que nenhum código lê a coluna. Migration aplicada
em qualquer banco não é editada nem apagada.

## Rota nova

- Grupo com `RequireAuthorization()` e a política do papel (`Politicas`).
  Rota pública (leitura do QR, webhook, cadastro, entrada) leva
  `AllowAnonymous()` e uma política de `RequireRateLimiting` própria, por IP
  (`Program.cs`, `AddRateLimiter`).
- POST autenticado já aceita `Idempotency-Key` pelo middleware
  `IdempotenciaDaRequisicao`; não há nada a fazer na rota. Só rota que devolve
  segredo ou escreve cookie leva `SemIdempotencia()`.
- Ação da operadora que mexe em dinheiro de revenda chama
  `AcoesSensiveisDaOperadora.Exigir("<ação>")` antes de gravar (ADR 0027).
- Situação e papel saem no JSON pelo nome (`JsonStringEnumConverter`); número
  só como número (`JsonNumberHandling.Strict`).
- Rota nova muda `web/openapi.json`: rodar `dotnet test` (o
  `ContratoDaApiTest` regenera o contrato e falha nessa primeira execução) e
  depois `cd web && npm run tipos`. O CI recusa contrato ou tipos desatualizados.

## Listagem

Rota GET que devolve lista que cresce com o uso nasce paginada: `PedidoDePagina`
na rota (`[AsParameters]`), `Pagina<T>` na resposta (`{ itens, total, proxima }`),
`PaginarAsync` na consulta, ordem total com o `Id` por último e `busca` aplicada
antes da página. Tamanho acima de 100 é reduzido no servidor
(`Paginacao.TamanhoMaximo`). Detalhes e a lista das rotas em `docs/SDD.md`
("Listagens paginadas"). A tela usa `useListaPaginada` e `ListaPaginada`. Teste
com mais registros que o máximo, horários iguais, e o isolamento por revenda e
por vendedor (ver `tests/AvaliaSales.Tests/Paginacao/PaginacaoTest.cs`).

## Nomes e texto

- Código em português sem acento (`RevendedorAtual`, `PrecoCents`); texto de
  tela e mensagem de erro com acento.
- Mensagem de `RegraDeNegocioException` segue a seção Texto do `DESIGN.md`:
  fato primeiro, sem artigo ("Mês fechado."), depois a saída no imperativo
  ("Use outra data."), até duas frases e cerca de 80 caracteres, sem "sua
  conta", "você", "por favor" nem "tente de novo"; nome do objeto, nunca id.
- Nome de teste é a regra em frase, com sublinhado
  (`Produto_de_um_revendedor_nao_aparece_para_outro`).
- Comentário explica o porquê, nunca repete o que a linha faz.

## Testes

- `dotnet test` roda tudo num processo, igual ao CI. Integração usa Postgres de
  verdade (fixture `ApiDeTeste` em `tests/AvaliaSales.Tests/Apoio`, um banco
  descartável por execução, com o papel `avalia_app_teste`, membro do
  `avalia_app`: as mesmas permissões e o RLS da produção).
- Cada trava de segurança tem teste que falha quando a trava é desligada.
  Conferir assim antes de confiar num teste novo de segurança.
- Integração externa tem teste de contrato HTTP contra servidor falso
  (`tests/AvaliaSales.Tests/Integracoes`). Simulador de Development existe para
  Pagar.me, Protheus e o conversor de PDF.

## Bibliotecas

Biblioteca nova só entra se tiver tudo isto: mantida pela Microsoft ou pela
.NET Foundation, ou com mais de 1.000 estrelas no GitHub; commit nos últimos 12
meses; licença MIT, Apache-2.0, BSD ou MPL; e uso real no que está sendo
construído. Versão vai em `Directory.Packages.props`, nunca no `.csproj`.

O que está instalado é o que o `Directory.Packages.props` lista; o SignalR do
servidor vem do framework. Validação, log estruturado e dados fictícios são
feitos sem biblioteca própria, e não há teste de camadas: antes de propor uma
biblioteca, mostrar o que ela resolve que o código atual não resolve.

Recusadas: NetArchTest (sem manutenção) e IdempotentAPI (sem manutenção; a
idempotência usa o próprio Postgres, skill `idempotencia`).

Ver também: `seguranca`, `idempotencia`, `codigo-morto`, `design-system`,
`pagarme-checkout-order`.
