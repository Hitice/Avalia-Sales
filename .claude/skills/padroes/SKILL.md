---
name: padroes
description: Padrões de construção do Avalia Sales (.NET em camadas, EF Core, Postgres com RLS). Consultar antes de criar entidade, tabela, caso de uso, rota, teste ou de adicionar biblioteca, para o código novo sair igual ao que existe e só com bibliotecas consolidadas.
---

# Padrões do Avalia Sales

## Onde cada coisa mora

| Projeto | Responsabilidade | Não pode |
| --- | --- | --- |
| `AvaliaSales.Domain` | Entidade, regra, `RegraDeNegocioException` | Depender de qualquer outro projeto ou pacote |
| `AvaliaSales.Application` | Caso de uso, DTO, interface do que precisa (`IBancoDeDados`, `IRevendedorAtual`) | Conhecer HTTP, Npgsql ou Identity |
| `AvaliaSales.Infrastructure` | `BancoDeDados`, migrations, Identity, clientes HTTP de integração | Ter regra de negócio |
| `AvaliaSales.Api` | Rota, autenticação, tradução de erro em status | Ter regra de negócio ou SQL |

Regra recusada sai do Domain como `RegraDeNegocioException`, com a mensagem
escrita para a tela, e a API devolve 422. Erro de programação é exceção comum e
vira 500.

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

Migration só aditiva: coluna nova nasce nula ou com padrão, e remoção vem numa
migration posterior, depois que nenhum código lê a coluna.

## Nomes e texto

- Código em português sem acento (`RevendedorAtual`, `PrecoCents`); texto de
  tela e mensagem de erro com acento.
- Nome de teste é a regra em frase, com sublinhado
  (`Produto_de_um_revendedor_nao_aparece_para_outro`).
- Comentário explica o porquê, nunca repete o que a linha faz.

## Testes

- `dotnet test` roda tudo num processo, igual ao CI. Integração usa Postgres de
  verdade (fixture `ApiDeTeste`, um banco descartável por execução).
- Cada trava de segurança tem teste que falha quando a trava é desligada.
  Conferir assim antes de confiar num teste novo de segurança.

## Bibliotecas

Biblioteca nova só entra se tiver tudo isto: mantida pela Microsoft ou pela
.NET Foundation, ou com mais de 1.000 estrelas no GitHub; commit nos últimos 12
meses; licença MIT, Apache-2.0, BSD ou MPL; e uso real na fase em andamento.
Versão vai em `Directory.Packages.props`, nunca no `.csproj`.

Aprovadas. Números conferidos na API do GitHub em 21/10/2026:

| Biblioteca | Para | Estrelas | Situação |
| --- | --- | --- | --- |
| EF Core + Npgsql | Banco | Microsoft | Instalada |
| ASP.NET Identity + JwtBearer | Login e token | Microsoft | Instalada |
| xUnit v3 | Testes | Microsoft.Testing.Platform | Instalada |
| [FluentValidation](https://github.com/FluentValidation/FluentValidation) | Validação de entrada na API | 9,8 mil | Entra na Fase 1 |
| [Serilog](https://github.com/serilog/serilog) | Log estruturado em JSON | 8,1 mil | Entra no deploy |
| [Microsoft.Extensions.Http.Resilience](https://github.com/dotnet/extensions) | Retentativa e tempo limite nas integrações, sobre o [Polly](https://github.com/App-vNext/Polly) (14,3 mil) | Microsoft | Instalada |
| [Testcontainers](https://github.com/testcontainers/testcontainers-dotnet) | Postgres em contêiner nos testes | 4,4 mil | Quando houver Docker |
| [ArchUnitNET](https://github.com/TNG/ArchUnitNET) | Teste das regras de camada | 1,4 mil | Entra na Fase 1 |
| [Bogus](https://github.com/bchavez/Bogus) | Dados fictícios do revendedor de demonstração | 9,7 mil | Quando houver demonstração |

Recusadas: NetArchTest (sem commit desde 07/2024) e IdempotentAPI (329
estrelas, sem commit desde 11/2024; ver a skill `idempotencia`).

Documentação: toda mudança de regra ou de arquitetura atualiza `docs/PDD.md`
ou `docs/SDD.md` no mesmo commit (seção Documentação do CLAUDE.md).

Ver também: `seguranca`, `idempotencia`, `codigo-morto`, `design-system`,
`pagarme-checkout-order`.
