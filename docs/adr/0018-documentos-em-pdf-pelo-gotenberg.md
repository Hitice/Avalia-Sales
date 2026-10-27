# 0018. Contrato e distrato em PDF pelo Gotenberg, a partir de HTML

- Situação - Aceita
- Data - 22/10/2026

## Contexto

O contrato de revenda passou a sair preenchido com os dados de cada revenda
(razão social, CNPJ, endereço, representante), e o distrato traz os clientes
ativos, o estoque e a conciliação da revenda que encerra. Um PDF fixo, gerado
uma vez à mão, não serve mais. O layout aprovado é HTML e CSS, impresso pelo
Chromium.

## Opções

- Gotenberg, um contêiner com o Chromium e uma API HTTP de HTML para PDF
- Playwright para .NET dentro da imagem do Worker
- Biblioteca de PDF em C# (QuestPDF, PDFsharp/MigraDoc), redesenhando o layout em código

## Decisão

Gotenberg 8 (`gotenberg/gotenberg:8.38.1-chromium`, licença MIT) no Compose de
produção, sem porta publicada e sem JavaScript. O contêiner está na rede do
Compose, mas o Chromium recusa endereço público e privado
(`CHROMIUM_DENY_PRIVATE_IPS` e `CHROMIUM_DENY_PUBLIC_IPS`): o documento só usa
os arquivos que o Worker manda junto.
Os modelos ficam em `src/AvaliaSales.Infrastructure/Impressao/Modelos`,
embutidos no assembly. `DocumentosEmPdf` troca `{{campo}}` pelo valor escapado
e `{{{campo}}}` pelo HTML montado no código (a tabela de clientes do distrato),
e manda o HTML, o rodapé, o CSS, o logo e as fontes (Charis SIL, licença OFL)
para `POST /forms/chromium/convert/html`. Só o Worker gera documento
(`DocumentosDaRevenda`, na rotina de retomadas): a falha do conversor não
derruba tela, e a próxima volta tenta de novo. Em Development e nos testes,
`Pdf:Simulado` gera um PDF mínimo sem o contêiner.

## Consequências

- O layout continua editável como HTML, e `docs/contrato/gerar.mjs` imprime um
  exemplo com o mesmo Chromium para revisão.
- Mais um contêiner no VPS, com o Chromium; o deploy baixa a imagem na primeira vez.
- Campo do modelo sem valor é erro, e o documento não sai com lacuna.
- A Playwright no Worker traria o Chromium para dentro da imagem da aplicação,
  com acesso à rede dela; a biblioteca em C# pediria redesenhar o layout aprovado.
