# 0020. Mês fechado garantido no banco, com a linha atrasada passada ao mês corrente

- Situação - Aceita
- Data - 24/10/2026

## Contexto

O dono fecha o mês na área Financeiro, e o número de um mês fechado não pode
mudar. A aplicação recusa a despesa e a retirada com data em mês fechado e
manda a correção ao mês corrente. Mas linhas do razão nascem em vários pontos
(webhook, Worker, venda, repasse), e o pagamento feito às 23h59 do último dia
pode ser processado depois de o dono fechar o mês: recusar essa linha faria o
Worker tentar para sempre.

## Opções

- Conferir o fechamento em cada ponto que grava no razão
- Recusar no banco toda linha em mês fechado
- Gatilho no banco que passa a linha de mês fechado ao mês corrente

## Decisão

Gatilho `lancamento_em_mes_aberto` (BEFORE INSERT em `lancamentos`): se a
competência da linha está fechada para a revenda, a linha vai à competência
corrente de Brasília; se a corrente também estiver fechada (o que a aplicação
não deixa acontecer, porque só mês que já acabou se fecha), recusa. A função
roda com o papel de quem grava, e o RLS de `fechamentos` limita a leitura à
revenda da linha. A aplicação continua conferindo antes, para dar a mensagem
certa na tela.

## Consequências

- Nenhum caminho de gravação, presente ou futuro, põe linha em mês fechado.
- O pagamento atrasado entra no mês corrente, como qualquer correção.
- A entidade em memória pode guardar a competência pedida, e não a gravada;
  quem precisar dela relê a linha.
