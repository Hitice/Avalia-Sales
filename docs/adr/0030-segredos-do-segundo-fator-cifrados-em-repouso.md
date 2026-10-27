# 0030. Segredos do segundo fator cifrados em repouso

- Situação - Aceita
- Data - 27/10/2026

## Contexto

O ASP.NET Identity grava a chave do app autenticador e os códigos de
recuperação em claro em `usuarios_tokens`. Quem lê o banco ou um backup gera os
códigos de qualquer conta com o segundo fator ligado, e o segundo fator deixa
de valer justamente no vazamento que ele deveria conter. A senha já fica em
hash; a chave do TOTP não pode, porque o servidor precisa dela para conferir o
código.

## Opções

- `ProtectPersonalData` do Identity com `ILookupProtector`
- Store do Identity com cifra só nos dois tokens, com a chave dos e-mails (ADR 0024)
- Chave nova só para o segundo fator

## Decisão

A segunda. `LojaDeUsuarios` estende a store do EF Core e sobrescreve
`GetTokenAsync` e `SetTokenAsync` só para `AuthenticatorKey` e `RecoveryCodes`
do provedor interno (`[AspNetUserStore]`). O Identity lê e grava os dois só por
esses métodos, e o resto da store fica igual. `CifraDosSegredosDoUsuario` usa
AES-256-GCM com uma subchave derivada por HKDF-SHA256 de
`Seguranca:ChaveDosEmails`, com rótulo próprio. O valor gravado fica no formato
`avalia-segredo:v1:` + base64(nonce, tag, cifra). O id do usuário e o nome do
token vão como dado associado, e o valor copiado para outra conta não abre.

- A leitura aceita os dois formatos; valor sem o prefixo é o gravado em claro.
- A gravação cifrada depende de `Seguranca:CifrarSegundoFator`, desligada por
  padrão. A versão que traz a cifra sobe com ela desligada; ligar vem depois,
  pelo `.env`. Assim a versão anterior no ar sempre lê o banco, a mesma regra
  das migrations: voltar para antes desta decisão é desligar, esperar o Worker
  decifrar e só então voltar (OPERACAO, Voltar).
- A rotina `RecifraDoSegundoFator` do Worker, a cada 10 minutos, deixa os dois
  tokens no formato da configuração. Ligada, cifra o que está em claro e, na
  rotação, o que a chave anterior cifrou: sem isso, a chave do app, que dura
  anos, deixaria de abrir quando `ChaveAnteriorDosEmails` sai do `.env` na
  rotação de 2 dias do ADR 0024. Desligada, decifra.
- A rotina troca cada linha só se o valor ainda é o que leu
  (`ExecuteUpdate` condicional): o código de recuperação gasto ou a chave nova
  gravados no meio da volta ficam, e a linha volta na próxima.
- Valor que nenhuma chave abre vai ao log uma vez por processo, com o id do
  usuário. O dono não entra com o app, e o suporte desliga o segundo fator pela
  linha de comando (`--desligar-segundo-fator`).

`ProtectPersonalData` foi descartado: cifra todos os dados pessoais marcados,
inclusive o e-mail e o nome de usuário, e pede um protetor de busca para o
login continuar achando a conta. Uma chave nova só para o segundo fator
acrescentaria uma variável obrigatória em produção sem ganho de isolamento,
porque a derivação já separa os dois usos.

## Consequências

- Quem tem só o banco ou o backup não gera código de nenhuma conta. Quem tem o
  banco e o `.env` gera.
- Perder `CHAVE_DOS_EMAILS` derruba também o segundo fator de todas as contas:
  o suporte desliga conta por conta, e cada dono liga de novo.
- A rotação da chave dos e-mails só termina depois que o Worker recifrou os
  segredos (OPERACAO, Chave dos e-mails sensíveis).
