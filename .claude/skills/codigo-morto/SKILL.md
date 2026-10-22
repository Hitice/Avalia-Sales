---
name: codigo-morto
description: Achar e remover código sem uso no Avalia Sales sem apagar código vivo. Usa o compilador (.editorconfig), o Roslynator e, no front, o knip, com a lista de falsos positivos já conferida nesta base. Usar em limpeza periódica, depois de refatorar ou quando uma ferramenta acusar símbolo sem uso.
---

# Código morto no Avalia Sales

Três camadas, da mais barata para a mais cara. Nenhuma remove nada sozinha:
cada achado passa pela conferência no fim.

## 1. Compilador, em todo build

O `.editorconfig` liga como aviso, e o `Directory.Build.props` trata aviso como
erro. O build já falha com:

| Regra | Acusa |
| --- | --- |
| IDE0051 | Membro privado sem uso |
| IDE0052 | Membro privado escrito e nunca lido |
| IDE0060 | Parâmetro sem uso |
| IDE0005 | `using` desnecessário |

`EnforceCodeStyleInBuild` e `AnalysisMode=Recommended` estão no
`Directory.Build.props`. Não há o que rodar além de `dotnet build`.

## 2. Roslynator, para o que é público ou interno

[Roslynator](https://github.com/dotnet/roslynator) (3,5 mil estrelas, mantido
na organização dotnet) acha símbolo sem referência em qualquer visibilidade.
Roda sem instalar, pelo `dotnet dnx`:

```bash
dotnet dnx roslynator.dotnet.cli --yes --allow-roll-forward -- \
  find-symbol AvaliaSales.slnx --unused --ignore-generated-code --exclude "**/Migrations/**"
```

O `--` antes de `find-symbol` é obrigatório: sem ele, o `dnx` engole as opções.

### Falsos positivos conhecidos (conferidos em 21/10/2026 e 22/10/2026)

Em 22/10/2026 o comando listou 413 símbolos: 276 propriedades de DTO e
entidade, construtores de serviço e de record, e métodos de rota. Só três eram
mortos (`Mensagem.MarcarLida`, `CodigoDaPlaca.RestricaoDaRota` e
`Renovacao.Cancelar`); o último revelou um defeito, a renovação esquecida que
travava a venda, e voltou a ser usado na correção. Para separar o que conferir:

```bash
... find-symbol ... -v d > /tmp/ros.txt
# classes, constantes e métodos, sem construtor vazio e sem teste
```

Em 21/10/2026 o comando acusou 21 símbolos, e nenhum era código morto:

| O que aparece | Por que está vivo |
| --- | --- |
| Construtor primário de serviço (`EmissorDeToken`, `Produtos`, `RevendedorNaConexao`) | Chamado pela injeção de dependência |
| Construtor privado sem parâmetro (`Produto()`, `Revendedor()`) | Usado pelo EF Core para materializar a entidade |
| Propriedade de entidade só gravada (`CriadoEm`) | Mapeada para coluna pelo EF Core |
| Propriedade de DTO ou record (`ProdutoDto.Ativo`, `TokenEmitido.Token`) | Lida pelo serializador JSON |
| Construtor de record de entrada (`PedidoDeEntrada`, `CadastrarProduto`) | Chamado pelo binding da rota |
| Método passado como grupo para a rota (`RotasDeAcesso.Entrar`) | Referenciado por `MapPost("/...", Entrar)`; o Roslynator não conta |
| `BancoDeDadosEmDesenho` | Achado por reflexão pelo `dotnet ef` |
| Construtor de classe de teste | Chamado pelo xUnit com a fixture |

Símbolo nessas categorias não sai sem a conferência abaixo mostrar que a
categoria não se aplica.

## 3. Front (React)

[knip](https://github.com/webpro-nl/knip) (12,5 mil estrelas, ISC) acha
arquivo, exportação e dependência sem uso em projeto TypeScript, com plugins para
Vite, Vitest e Storybook:

```bash
npx knip            # na pasta do front
npx knip --production   # ignora testes e configuração de desenvolvimento
```

Configuração em `web/knip.json`: ignora o tipo gerado (`src/api/esquema.d.ts`)
e os componentes copiados do shadcn (`src/components/ui`), cujas exportações
são a interface da biblioteca, e conta como uso a exportação usada no próprio
arquivo. Em 22/10/2026 saiu limpo, depois de remover `radio-group.tsx` e
esquemas e apelidos de tipo sem uso.

## Conferência antes de apagar

1. Procurar o nome em todo o repositório, inclusive em string, JSON e YAML:
   ```bash
   git grep -n "<Nome>"
   ```
   Uso por reflexão, por nome de rota ou por configuração aparece aqui e não no
   compilador.
2. Apagar, rodar `dotnet build` e `dotnet test` inteiros.
3. Coluna e tabela não saem junto com a classe: migration só aditiva. A coluna
   deixa de ser lida num commit e sai numa migration posterior.
4. Migration nunca é apagada nem editada depois de aplicada em qualquer banco.
5. Um commit por remoção lógica, com o motivo na mensagem.
