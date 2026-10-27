---
name: seguranca
description: Revisão de segurança do Avalia Sales pelo OWASP ASVS 5.0, com o comando que prova cada item. Usar antes de publicar rota, tabela, integração ou tela nova, e em varredura periódica. Cobre isolamento entre revendedores, token, segredos, dependências, webhooks e LGPD.
---

# Segurança do Avalia Sales

A referência é o [OWASP ASVS 5.0](https://github.com/OWASP/ASVS), com as
[Cheat Sheets da OWASP](https://cheatsheetseries.owasp.org/IndexASVS.html) para
o como fazer. Cada item tem o capítulo do ASVS e a prova. Item sem prova não
conta como feito. O que está em produção e por quê está em `docs/SDD.md`,
seção 9 (Segurança e LGPD).

## O risco número um: um revendedor ver dado de outro (V8 Autorização)

- [ ] Entidade nova implementa `IDoRevendedor` e a migration chama
  `IsolarPorRevendedor`. Prova: `Toda_tabela_com_revendedor_tem_rls_ligado_e_forcado`.
- [ ] O revendedor sai só da sessão (`IRevendedorAtual`), nunca do corpo da
  requisição. DTO de entrada da revenda não tem `RevendedorId`; só a operadora
  recebe `RevendedorId` em DTO, porque age sobre uma revenda (hoje
  `Financeiro/AcertosDeRevenda.cs`). Prova:
  ```bash
  grep -rnE "record \w+\([^)]*Guid RevendedorId" src/AvaliaSales.Application --include='*.cs' | grep -v Dto
  ```
  Resultado esperado: só os records da operadora (`NovoAcerto`) e os de leitura
  que ela monta.
- [ ] Rota nova com dado de revendedor fica num grupo com `RequireAuthorization()`
  e a política do papel. Prova: teste de 401 sem token e de 403 com o papel errado.
- [ ] A API conecta como `avalia_app` (sem superusuário e sem BYPASSRLS). Prova:
  ```sql
  select rolsuper, rolbypassrls from pg_roles where rolname = 'avalia_app';
  ```
  Esperado: `f | f`. O teste `Isolamento/PermissoesDoBancoTest.cs` cobra o mesmo.
- [ ] Teste de isolamento pela API para cada recurso novo: grava no A, lista no B,
  B vê vazio. Desligar a trava uma vez e ver o teste falhar.
- [ ] Vendedor lê só o que é dele: lista paginada filtrada pelo vendedor e dado
  de cliente alheio mascarado. Prova: `Paginacao/PaginacaoTest.cs`
  (`...o_vendedor_so_pagina_as_dele`).
- [ ] Ação da operadora que mexe em dinheiro de revenda chama
  `AcoesSensiveisDaOperadora.Exigir` (segundo fator ligado; ADR 0027). Prova:
  teste da ação com a conta sem segundo fator recebendo 422.

## Autenticação, sessão e token (V6, V7, V9)

- [ ] Token de acesso de 15 minutos (`Jwt:MinutosDeValidade`, máximo 60),
  HMAC-SHA256, emissor e público validados. Chave de 32 caracteres ou mais
  (`OpcoesJwt`, `MinLength(32)`), lida do ambiente (`Jwt__Chave`), nunca do
  repositório.
- [ ] Refresh token rotativo em cookie `HttpOnly`, `Secure`, `SameSite=Strict`;
  token reapresentado encerra a sessão (ADR 0006). Prova: `Acesso/SessaoTest.cs`
  e `Acesso/ToleranciaDoRefreshTest.cs`.
- [ ] Login com resposta igual para e-mail inexistente e senha errada, bloqueio
  após 5 erros (`Lockout.MaxFailedAccessAttempts`) e limite por minuto por IP
  (`Limites:EntradasPorMinuto`, padrão 20). Prova: `Acesso/EntradaTest.cs` e
  `curl` repetido devolvendo 429.
- [ ] Senha por tamanho mínimo (10, `Password.RequiredLength`), sem regra de tipo
  de caractere. O ASVS 5.0 (V6.2.1) pede 8 e recomenda 15; a NIST SP 800-63B-4
  pede 15 quando a senha é o único fator, caso do vendedor, que não liga o
  segundo fator.
- [ ] Segundo fator por TOTP ou código por e-mail, com desafio de 5 minutos
  (ADR 0017). Prova: `Acesso/SegundoFatorTest.cs`, `Acesso/CodigoPorEmailTest.cs`.
- [ ] Cadastro público com confirmação por link, captcha Turnstile e
  `Limites:CadastrosPorHoraPorIp` (padrão 10). Prova:
  `Acesso/CadastroPublicoTest.cs`, `Acesso/CadastroComCaptchaTest.cs`.

## Entrada e regra de negócio (V1, V2, V4)

- [ ] SQL só pelo EF Core ou com parâmetro. Prova:
  ```bash
  grep -rnE 'FromSqlRaw|ExecuteSqlRaw|SqlQueryRaw|CommandText *=' src --include='*.cs' | grep -v /obj/
  ```
  Cada resultado precisa de parâmetro, nunca de texto concatenado com entrada.
  `FromSql`, `ExecuteSql` e `SqlQuery` com `$"..."` ou `$"""..."""` viram
  parâmetro sozinhos; os `*Raw` e o `CommandText` não.
- [ ] Regra no Domain (`RegraDeNegocioException`, 422); formato na borda. A tela
  nunca é a única validação.
- [ ] Dinheiro chega em centavos inteiros; valor negativo ou absurdo é recusado
  no Domain. JSON com número em texto é recusado
  (`JsonNumberHandling.Strict`).
- [ ] Gravação repetida não cobra nem lança duas vezes: `Idempotency-Key` pelo
  middleware `IdempotenciaDaRequisicao` (skill `idempotencia`). Rota nova com
  `SemIdempotencia()` precisa devolver segredo ou escrever cookie.
- [ ] Destino de placa e link limitados por dia e conferidos contra a lista de
  destinos bloqueados (`LimiteDeDestinos`, `DestinosBloqueados`; ADR 0026).
- [ ] Logo do revendedor (V5) só PNG ou WebP, com tamanho máximo
  (`LogoDoRevendedor`); SVG recusado.

## Segredos e configuração (V13)

- [ ] Nenhum segredo no Git. O job `segredos` do CI roda o gitleaks no
  histórico; falso positivo entra no `.gitleaksignore` com o motivo. Local:
  ```bash
  gitleaks git --redact -v
  gitleaks dir --redact -v .
  ```
- [ ] `.env` no `.gitignore`; `.env.example` só com nomes. Prova:
  `git check-ignore -v .env`.
- [ ] No Compose de produção só o Nginx publica porta (80 e 443). Prova:
  `grep -n "ports:" -A 2 docker-compose.producao.yml`. Os contêineres têm
  saída para a internet (não há rede `internal`); o Gotenberg roda com
  `CHROMIUM_DENY_PRIVATE_IPS`, `CHROMIUM_DENY_PUBLIC_IPS` e
  `CHROMIUM_DISABLE_JAVASCRIPT`, que barram o que o Chromium abre.

## Dependências e imagem (V15)

- [ ] Nenhum pacote vulnerável. O restore já audita (NuGetAudit) e o build trata
  aviso como erro. Prova:
  ```bash
  dotnet list package --vulnerable --include-transitive
  cd web && npm audit --omit=dev
  ```
- [ ] Imagem e arquivos sem vulnerabilidade alta. Passo manual, fora do CI,
  antes de trocar imagem base: o Trivy não está instalado nas máquinas nem no
  VPS (`brew install trivy`). Prova, com
  [Trivy](https://github.com/aquasecurity/trivy):
  ```bash
  trivy fs --scanners vuln,secret,misconfig .
  trivy image avalia-sales-api:local   # depois do docker compose build
  ```
- [ ] Contêiner roda sem root (`USER $APP_UID` no Dockerfile).

## Cabeçalhos e frontend (V3)

- [ ] CSP, HSTS, `Permissions-Policy` e `frame-ancestors 'none'` no Nginx
  (`infra/nginx/avalia-sales.conf.template`). Domínio novo em integração
  (script, `connect-src`, `frame-src`) entra na CSP no mesmo commit. Prova:
  `curl -sI https://<dominio>/ | grep -iE "content-security|strict-transport|permissions-policy"`.
- [ ] Swagger e OpenAPI só em Development.
- [ ] Cartão tokenizado no navegador: a API nunca recebe número nem CVV.

## Webhooks e integrações (V4, V16)

- [ ] Webhook só grava na caixa de entrada e responde 200; o Worker reconsulta
  a fonte (skill `idempotencia`, ADR 0005).
- [ ] Rota de webhook com `RequireRateLimiting` (120 por minuto por IP) e
  corpo máximo de 1 MiB (`RotasDeWebhook.CorpoMaximoBytes`, 413 acima). O
  Pagar.me não assina o webhook: o segredo vai na URL e a rota responde 404
  sem ele, em comparação de tempo constante.

## Logs, erros e LGPD (V14, V16)

- [ ] Erro 500 não devolve pilha nem SQL ao cliente (ProblemDetails padrão).
- [ ] Log e Sentry sem senha, token, documento, dado de cartão, corpo, cookie
  nem IP. Prova:
  ```bash
  grep -rniE "Log(Information|Warning|Error|Debug).*(senha|token|cpf|cnpj|cartao)" src --include='*.cs'
  ```
- [ ] E-mail com link de senha ou código tem o conteúdo apagado depois do
  envio, e o sensível fica cifrado em repouso (ADR 0024).
- [ ] Dado pessoal mínimo; exportação (`GET /clientes/{id}/dados`) e
  anonimização (`POST /clientes/{id}/anonimizacao`) só pelo papel Dono e dentro
  da revenda (SDD, seção 9).

## Fora deste checklist

- `/security-review` revisa as mudanças locais ainda não enviadas ao `main`.
- O job `qualidade` do CI manda o C# e o TypeScript ao SonarCloud, em
  `continue-on-error`: não segura o Deploy.
