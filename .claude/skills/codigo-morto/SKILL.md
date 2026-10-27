---
name: codigo-morto
description: Achar e remover código sem uso no Avalia Sales sem apagar código vivo. Usa o compilador (.editorconfig), o Roslynator e, no front, o knip, com a lista de falsos positivos já conferida nesta base. Usar em limpeza periódica, depois de refatorar ou quando uma ferramenta acusar símbolo sem uso.
---

# Código morto no Avalia Sales

Três camadas, da mais barata para a mais cara. Nenhuma remove nada sozinha:
cada achado passa pela conferência no fim.

## 1. Compilador, em todo build

O `.editorconfig` liga como aviso, e o `Directory.Build.props` trata aviso como
erro (`TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`,
`AnalysisMode=Recommended`). O build já falha com:

| Regra | Acusa |
| --- | --- |
| IDE0051 | Membro privado sem uso |
| IDE0052 | Membro privado escrito e nunca lido |
| IDE0060 | Parâmetro sem uso |
| IDE0005 | `using` desnecessário |

Não há o que rodar além de `dotnet build`. Migrations ficam de fora
(`generated_code = true` no `.editorconfig`).

## 2. Roslynator, para o que é público ou interno

[Roslynator](https://github.com/dotnet/roslynator) acha símbolo sem referência
em qualquer visibilidade. Roda sem instalar, pelo `dotnet dnx` do SDK 10:

```bash
dotnet dnx roslynator.dotnet.cli --yes --allow-roll-forward -- \
  find-symbol AvaliaSales.slnx --unused --ignore-generated-code --exclude "**/Migrations/**"
```

O `--` antes de `find-symbol` é obrigatório: sem ele, o `dnx` engole as opções.
Sem `@versão`, o `dnx` baixa a última versão do pacote: confira se a saída
mudou de formato antes de filtrar. A saída é longa, e quase tudo é falso positivo das
categorias abaixo. Filtrar para classe, constante e método, sem construtor
vazio e sem teste, antes de conferir um a um.

### Falsos positivos conhecidos

| O que aparece | Por que está vivo |
| --- | --- |
| Construtor primário de serviço (`EmissorDeToken`, `Catalogo`, `RevendedorNaConexao`) | Chamado pela injeção de dependência |
| Construtor privado sem parâmetro (`Produto()`, `Revendedor()`) | Usado pelo EF Core para materializar a entidade |
| Propriedade de entidade só gravada (`CriadoEm`) | Mapeada para coluna pelo EF Core |
| Propriedade de DTO ou record (`ItemDoCatalogo.Nome`, `TokenEmitido.Token`) | Lida pelo serializador JSON |
| Construtor de record de entrada (`PedidoDeEntrada`, `AjustarProduto`) | Chamado pelo binding da rota |
| Método passado como grupo para a rota (`RotasDeAcesso.Entrar`) | Referenciado por `MapPost("/...", Entrar)`; o Roslynator não conta |
| `BancoDeDadosEmDesenho` | Achado por reflexão pelo `dotnet ef` |
| Construtor de classe de teste | Chamado pelo xUnit com a fixture |
| Método de entidade só chamado por um fluxo raro (cancelar, estornar) | Pode ser regra de negócio ainda não ligada à tela: conferir no PDD antes de apagar |

Símbolo nessas categorias não sai sem a conferência abaixo mostrar que a
categoria não se aplica.

## 3. Front (React)

[knip](https://github.com/webpro-nl/knip) acha arquivo, exportação e
dependência sem uso em projeto TypeScript, com plugins para Vite, Vitest e
Storybook. Não está nas dependências nem no CI: roda pelo `npx`, na versão
maior do schema de `web/knip.json`.

```bash
cd web
npx knip@5                # tudo
npx knip@5 --production   # ignora testes e configuração de desenvolvimento
```

Configuração em `web/knip.json`: ignora o tipo gerado (`src/api/esquema.d.ts`)
e os componentes copiados do shadcn (`src/components/ui`), cujas exportações
são a interface da biblioteca, e conta como uso a exportação usada no próprio
arquivo.

## Conferência antes de apagar

1. Procurar o nome em todo o repositório, inclusive em string, JSON e YAML:
   ```bash
   git grep -n "<Nome>"
   ```
   Uso por reflexão, por nome de rota ou por configuração aparece aqui e não no
   compilador.
2. Apagar, rodar `dotnet build` e `dotnet test` inteiros (e, no front, a
   lista de `.claude/rules/web.md`).
3. Coluna e tabela não saem junto com a classe: migration só aditiva. A coluna
   deixa de ser lida num commit e sai numa migration posterior.
4. Migration já commitada nunca é apagada nem editada (`.claude/rules/migrations.md`).
5. Um commit por remoção lógica, com o motivo na mensagem.
