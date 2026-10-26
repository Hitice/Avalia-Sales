# Avalia Sales: plataforma de revenda

Plataforma multi-revendedor de placas com QR Code e NFC. Regra de negócio e
escopo estão em `docs/PDD.md`; arquitetura em `docs/SDD.md`; cada decisão
estrutural em `docs/adr` (MADR). O desenho técnico inicial
(https://claude.ai/artifact/PhVBynCUBEJXdcWYNhzury) é só o registro da
concepção: o repositório vale mais que ele.

Ler o documento certo antes de decidir arquitetura, regra ou escopo. Se o
código e o documento discordarem, perguntar qual vale e atualizar o outro.

## Stack

- .NET 10 LTS: ASP.NET Core Web API, Worker Service, EF Core 10, ASP.NET
  Identity + JWT, SignalR. Solução em Api, Application, Domain, Infrastructure e
  Worker; Domain não depende de nenhum outro projeto. A versão mora só em
  `Directory.Build.props` e `Directory.Packages.props`; o .NET 8 sai de suporte
  em 10/11/2026.
- PostgreSQL 17, com `revendedor_id` em toda tabela de negócio, filtro global
  do EF Core e RLS. A API conecta com o papel `avalia_app`, sem superusuário e
  sem BYPASSRLS; quem migra é o dono do banco.
- Front único em React 19 + Vite: painel do revendedor e da Avalia, app do
  vendedor (PWA com Web NFC e câmera) e página do QR, como rotas do mesmo app.
  React Router, TanStack Query, Zustand, React Hook Form + Zod, Tailwind 4 e
  componentes no modelo do shadcn/ui. White label por variáveis CSS (skill
  `design-system`).
- Integrações: Pagar.me v5 (checkout, split e cupom; marketplace na conta da
  Avalia), D4Sign, Resend, Google Maps JavaScript + Places (New), BrasilAPI
  (CNPJ e CEP no cadastro), Gotenberg (PDF do contrato e do distrato), Protheus
  por simulador.
- Infra: VPS Linux, Docker Compose, Nginx, Let's Encrypt; GitHub Actions com
  self-hosted runner, SonarCloud, Sentry.

## Regras 

- Revendedor é o tenant. A Avalia é operadora da plataforma e também uma
  revendedora comum: nenhuma regra do código pergunta se o revendedor é a
  Avalia.
- Um domínio só para todos os revendedores; o tenant vem do login.
- Dinheiro em centavos (bigint, colunas `_cents`), taxa em pontos base
  (`_bps`). Preço, comissão e taxa ficam gravados na venda.
- Razão só recebe inserção; correção é estorno.
- Webhook grava o evento cru com chave única antes de processar e reconsulta a
  API de origem.
- `avaliaone.com.br/q/{codigo}` responde para sempre. O Sales do Avalia não é
  tocado até a virada.
- Registro novo nasce com criar, editar e excluir na tela.

## Rodar e testar

- `dotnet test` roda a suíte inteira, num processo só, igual ao CI. Precisa de
  um Postgres: local em `localhost` com o usuário do sistema, ou outro pela
  variável `AVALIA_SALES_PG_ADMIN`. Cada execução cria e apaga o próprio banco.
- Postgres local do Homebrew, sem serviço fixo:
  `pg_ctl -D /opt/homebrew/var/postgresql@17 start` (e `stop`).
- Tabela nova com `revendedor_id` precisa de `IsolarPorRevendedor` na
  migration; o teste `Toda_tabela_com_revendedor_tem_rls_ligado_e_forcado`
  falha sem ele.
- Migration nova: `dotnet ef migrations add <Nome> -p src/AvaliaSales.Infrastructure -s src/AvaliaSales.Infrastructure -o Banco/Migrations`.

## Avalia, o repositório vizinho

`~/Documents/Avalia` é o sistema atual (Laravel 12). Para ler as regras do
Sales, abrir a sessão com:

```bash
cd ~/Documents/development/Avalia-Sales
claude --add-dir ~/Documents/Avalia
```

As regras de venda estão em `app/Actions/Etiquetas` e em
`.claude/skills/sales/SKILL.md` de lá. É só leitura: este projeto não escreve no
Avalia nem lê o banco de produção dele.

## Escrita

- Português técnico e direto, sem travessão e sem metáfora. Em documento,
  rótulo e explicação separados por " - ".
- Documento descreve o produto e as decisões técnicas, sem bastidor do
  desenvolvimento: nada de falha passada de outro sistema nem de motivo pessoal
  do projeto. A Avalia pode aparecer comercialmente.
- Commit em inglês, no formato Conventional Commits (`feat:`, `fix:`, `chore:`,
  `docs:`, `refactor:`, `test:`, `ci:`; escopo opcional), resumo no imperativo.
- Mensagem de commit escrita em arquivo e enviada com `git commit -F`, só com o
  autor configurado no repositório: sem linha `Co-Authored-By` e sem rodapé de
  ferramenta, nem em PR.

## Documentação (PDD e SDD)

- `docs/PDD.md` é o produto: papéis, jornada, telas, regras de negócio,
  dinheiro, fases e decisões pendentes.
- `docs/SDD.md` é o software: arquitetura, isolamento, modelo de dados,
  integrações, segurança, infra, CI e decisões técnicas.
- Toda mudança de regra, comportamento, tabela, integração, configuração ou
  decisão atualiza o documento certo no mesmo commit, com uma linha no
  registro de mudanças do fim dele. Refatoração sem efeito visível não precisa.
- O que está no código vale mais que o documento: se discordarem, corrigir o
  documento no mesmo trabalho, ou perguntar se for regra de negócio.
- Decisão estrutural nova (tecnologia, isolamento, integração, segurança)
  ganha um ADR em `docs/adr` e uma linha no índice dele.
- Três travas cobram isso: o hook de fim de turno do Claude Code
  (`.claude/hooks/documentacao.sh`), o passo "Documentação" do CI em pull
  request e esta seção.
