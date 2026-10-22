---
name: seguranca
description: Revisão de segurança do Avalia Sales pelo OWASP ASVS 5.0, com o comando que prova cada item. Usar antes de publicar rota, tabela, integração ou tela nova, e em varredura periódica. Cobre isolamento entre revendedores, token, segredos, dependências, webhooks e LGPD.
---

# Segurança do Avalia Sales

A referência é o [OWASP ASVS 5.0](https://github.com/OWASP/ASVS) (17
capítulos, versão de 15/10/2026), com as [Cheat Sheets da
OWASP](https://cheatsheetseries.owasp.org/IndexASVS.html) para o como fazer.
Cada item abaixo tem o capítulo do ASVS e a prova. Item sem prova não conta
como feito.

## O risco número um: um revendedor ver dado de outro (V8 Autorização)

- [ ] Entidade nova implementa `IDoRevendedor` e a migration chama
  `IsolarPorRevendedor`. Prova: `Toda_tabela_com_revendedor_tem_rls_ligado_e_forcado`.
- [ ] O revendedor sai só do token (`RevendedorDoToken`). Nenhum DTO de entrada
  tem `RevendedorId`. Prova:
  ```bash
  grep -rn "RevendedorId" src/AvaliaSales.Application --include='*.cs' | grep -i "record\|comando"
  ```
  Resultado esperado: vazio.
- [ ] Rota nova com dado de revendedor fica num grupo com `RequireAuthorization()`.
  Prova: teste de 401 sem token para a rota.
- [ ] A API conecta como `avalia_app` (sem superusuário e sem BYPASSRLS). Prova:
  ```sql
  select rolsuper, rolbypassrls from pg_roles where rolname = 'avalia_app';
  ```
  Esperado: `f | f`.
- [ ] Teste de isolamento pela API para cada recurso novo: grava no A, lista no B,
  B vê vazio. Desligar a trava uma vez e ver o teste falhar.

## Autenticação e token (V6, V9)

- [ ] Token de 15 minutos, HMAC-SHA256, emissor e público validados,
  `ValidAlgorithms` fixo. Chave de 32 caracteres ou mais, lida do ambiente
  (`Jwt__Chave`), nunca do repositório.
- [ ] Login com resposta igual para e-mail inexistente e senha errada, bloqueio
  após 5 erros e limite de 10 tentativas por minuto por IP. Prova: testes em
  `Acesso/EntradaTest.cs` e `curl` repetido devolvendo 429.
- [ ] Senha por tamanho mínimo (10), sem regra de tipo de caractere, como pede o
  NIST SP 800-63B.
- [ ] Refresh token, quando entrar: rotativo, em cookie `HttpOnly`, `Secure`,
  `SameSite=Strict`, revogado no uso repetido.

## Entrada e regra de negócio (V1, V2, V4)

- [ ] SQL só pelo EF Core ou com parâmetro. Prova:
  ```bash
  grep -rnE "FromSqlRaw|ExecuteSqlRaw|\\\$\"(select|insert|update|delete)" src --include='*.cs'
  ```
  Cada resultado precisa de parâmetro, nunca de texto concatenado com entrada.
- [ ] Validação de formato na API com FluentValidation (Fase 1) e regra no
  Domain. A tela nunca é a única validação.
- [ ] Dinheiro chega em centavos inteiros; valor negativo ou absurdo é recusado
  no Domain.

## Segredos e configuração (V13)

- [ ] Nenhum segredo no Git. Prova, com [gitleaks](https://github.com/gitleaks/gitleaks)
  (30 mil estrelas, MIT):
  ```bash
  brew install gitleaks   # uma vez
  gitleaks git --redact -v
  gitleaks dir --redact -v .
  ```
- [ ] `.env` no `.gitignore`; `.env.example` só com nomes. Prova:
  `git check-ignore -v .env`.
- [ ] Postgres e API expostos só em `127.0.0.1` no Compose; o Nginx é a única
  porta pública no VPS.

## Dependências e imagem (V15)

- [ ] Nenhum pacote vulnerável. O restore já audita (NuGetAudit) e o build trata
  aviso como erro. Prova:
  ```bash
  dotnet list package --vulnerable --include-transitive
  ```
- [ ] Imagem e arquivos sem vulnerabilidade alta. Prova, com
  [Trivy](https://github.com/aquasecurity/trivy) (38,6 mil estrelas, Apache-2.0):
  ```bash
  brew install trivy      # uma vez
  trivy fs --scanners vuln,secret,misconfig .
  trivy image avalia-sales-api   # depois do docker compose build
  ```
- [ ] Contêiner roda sem root (`USER $APP_UID` no Dockerfile).

## Cabeçalhos e frontend (V3)

- [ ] Quando o primeiro front subir: CSP, HSTS, `X-Content-Type-Options`,
  `Referrer-Policy` e `frame-ancestors`, com
  [NetEscapades.AspNetCore.SecurityHeaders](https://github.com/andrewlock/NetEscapades.AspNetCore.SecurityHeaders)
  (854 estrelas, MIT, mantido pelo autor de *ASP.NET Core in Action*) ou no Nginx.
  Prova: `curl -sI https://<dominio>/ | grep -iE "content-security|strict-transport"`.
- [ ] Logo do revendedor só PNG ou WebP, com tamanho máximo; SVG recusado.

## Webhooks e integrações (V4, V16)

- [ ] Regras de idempotência e reconsulta: skill `idempotencia`.
- [ ] Rota de webhook com limite de requisições e corpo máximo.

## Logs, erros e LGPD (V14, V16)

- [ ] Erro 500 não devolve pilha nem SQL ao cliente (ProblemDetails padrão).
- [ ] Log sem senha, token, documento ou dado de cartão. Prova:
  ```bash
  grep -rniE "Log(Information|Warning|Error|Debug).*(senha|token|cpf|cnpj|cartao)" src --include='*.cs'
  ```
- [ ] Dado pessoal mínimo e anonimização do comprador pela tela do dono (desenho
  técnico, seção Segurança e LGPD).

## Fora deste checklist

- `/security-review` revisa o que mudou na branch.
- O SonarCloud analisa cada pull request quando o CI estiver ligado a ele.
