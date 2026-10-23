# Avalia Sales - documento de produto (PDD)

O que a plataforma faz, para quem, e as regras de negócio. O como fica no
[SDD](SDD.md). Os dois são atualizados no mesmo commit da mudança que os
afeta, com uma linha no registro de mudanças do fim.

Situação em 23/10/2026 - em produção em sales.avaliaone.com.br (versão 1.8),
com cadastro e contrato pela D4Sign (ambiente de testes), catálogo e pedido de
placas, lote, equipe, venda no balcão com QR e NFC, serviços, chamados,
notificações, marca da revenda, auditoria e encerramento de conta pelo
distrato. A cobrança pelo Pagar.me espera a conta da Avalia, e o mapa espera a
chave do Google para o navegador (seção 11).

## 1. Visão

Plataforma para revender placas com QR Code e NFC que levam o cliente a uma
avaliação no Google, a um link ou a uma página de links. A Avalia opera a
plataforma e vende as placas aos revendedores. Cada revendedor vende com a
própria equipe, recebe pelo Pagar.me e acompanha as vendas num painel.

## 2. Papéis

| Papel | Quem é | O que faz |
| --- | --- | --- |
| Operadora | A Avalia | Vende placas aos revendedores, recebe a taxa de cada venda, produz e despacha lotes, executa os serviços vendidos, atende os chamados |
| Revendedor (dono) | Empresa que revende | Compra placas, convida vendedores, define preço e comissão, acompanha painel e mapa |
| Vendedor | Equipe do revendedor | Vende no balcão pelo celular: lê o QR e grava o NFC |
| Cliente final | Quem compra a placa | Paga por Pix ou cartão e usa a placa no negócio |

A Avalia também é revendedora, com o mesmo peso de qualquer outra: mesmo fluxo,
mesma taxa, usuários separados dos da operadora. Nenhuma regra pergunta se o
revendedor é a Avalia.

## 3. Modelo de negócio

| Dinheiro | Quem paga | Quem recebe | Quando |
| --- | --- | --- | --- |
| Pedido de placas | Revendedor | Avalia | Antes do envio do lote |
| Venda da placa | Cliente final | Avalia a taxa e o revendedor o resto, pelo split do Pagar.me | No pagamento |
| Renovação | Cliente final | Avalia a taxa e o revendedor o resto, sem comissão | No pagamento |
| Comissão | Revendedor | Vendedor, por Pix, registrado no painel | Quando o revendedor repassa |

- Taxa da Avalia - 1,99% do valor bruto de cada venda e renovação paga,
  gravada na cobrança no momento dela (contrato, cláusula 5.4).
- Pacotes de placas - configuráveis pela operadora - 20, 50 e 100 placas a
  R$ 23,90, R$ 16,90 e R$ 13,90 a placa (R$ 478,00, R$ 845,00 e R$ 1.390,00),
  e um de 10 para o teste da revendedora Avalia. O preço do pacote já cobre a
  produção e a margem da Avalia; na venda, a Avalia fica só com a taxa.
- Catálogo - a Avalia cadastra os produtos (placa, placa com serviço e
  serviço) com foto, preço sugerido e situação (disponível, esgotado, fora de
  linha). O revendedor não cria produto: define o preço dele, livre, nunca
  abaixo do custo - na placa, o que pagou por ela no pacote (sem estoque, o
  preço por placa do pacote mais caro); no serviço, só maior que zero. Só a
  placa é comprada no atacado.
- Serviço vendido - a venda paga de serviço (Perfil no Google, cardápio,
  landing page, sistema, kit mesa, QR dinâmico) vira ordem de serviço para a
  Avalia executar, com a situação a fazer, em andamento, entregue ou
  cancelada, que o revendedor acompanha. A Avalia recebe a taxa da venda.
- Taxa do Pagar.me - paga pelo revendedor.
- Exemplo - venda de R$ 100,00 com comissão de 20%: R$ 1,99 para a Avalia,
  R$ 98,01 para o revendedor (de onde sai a taxa do Pagar.me), e R$ 20,00
  lançados como devidos ao vendedor.

## 4. Jornada do revendedor

1. Cadastro - pela tela, com CNPJ, empresa, celular, endereço e o login do dono.
   O CNPJ válido preenche a razão social e o endereço (BrasilAPI, com os dados da
   Receita), e o CEP preenche rua, bairro, cidade e UF; o dono só confere. A conta para
   receber no Pagar.me é informada pela operadora até o cadastro de recebedor
   pela API entrar.
2. Contrato - criado sozinho depois do cadastro e enviado pela D4Sign ao e-mail
   do dono. Sem contrato assinado, não há pedido. Contrato assinado em papel é
   registrado pela operadora.
3. Pedido - "Pedir placas" no catálogo ou em Pedidos: pacote, cupom opcional,
   pago antes do envio pelo checkout do Pagar.me (Pix, cartão ou boleto, tudo
   para a Avalia). A operadora também pode registrar um pagamento recebido por
   fora. Situação do pedido - aguardando pagamento, pago, em produção,
   enviado, entregue (ou cancelado).
4. Envio - a Avalia recebe o pedido por e-mail, no sino e na fila, marca em
   produção e despacha um lote da mesma quantidade; o dono recebe o e-mail e a
   notificação com o número do lote.
5. Ativação - o revendedor digita o número do lote (LT-0001, lt 1 ou 1); as
   placas entram no estoque dele com o custo do pacote, e o pedido fica entregue.
6. Equipe - cadastro dos vendedores e da comissão de cada um.
7. Venda - o vendedor busca o negócio no Google (ou cola o link de avaliação
   do perfil, ou o link do cliente), lê o QR da placa ou digita o código,
   preenche o cliente e cobra por Pix ou cartão. No fim grava na tag NFC o
   mesmo endereço do QR (`/Q/{codigo}`).
8. Pagamento - o cliente paga e o split divide na hora.
9. Painel - vendas ao vivo, saldo de comissão, gráficos e mapa dos lugares vendidos.
10. Encerramento - o dono pode encerrar a conta pelo distrato (seção 6).

## 5. Telas

Todas no mesmo app, liberadas pelo papel do usuário.

| Tela | Quem usa | Pensada para | O que faz |
| --- | --- | --- | --- |
| Entrada, cadastro e senha | Todos | Celular e computador | Entrar, cadastrar a revenda (CNPJ preenche razão social e endereço), confirmar o e-mail, criar e trocar a senha; cadastro temporário de operadora com e-mail @avaliaone.com.br |
| Início | Dono | Computador | Números do mês e gráficos - vendas por dia nos últimos 30 dias, recebido por mês nos últimos 6 e vendas do mês por vendedor |
| Vendas | Dono | Computador | Lista das vendas, cobrança de novo, cancelamento, cortesia, recebido por fora |
| Clientes | Dono | Computador | Clientes da revenda, exportação dos dados e anonimização (LGPD) |
| Serviços | Dono e vendedor | Celular | Abas Link (link curto /L/ de qualquer endereço colado), NFC (grava o link curto na tag), Maps (busca o negócio no Google e cria o link curto da avaliação) e Status (só o dono; as ordens dos serviços vendidos que a Avalia executa); o dono edita, desliga e exclui o link |
| Mapa | Dono | Computador | Lugares vendidos, no Google Maps |
| Catálogo | Dono e vendedor | Computador e celular | Produtos da Avalia com foto, preço sugerido e o preço da revenda; o dono ajusta o preço e pede placas; a equipe vende daqui (serviço abre a venda sem placa) |
| Estoque | Dono | Computador | Placas por situação, consignação ao vendedor e ativação de lote |
| Pedidos | Dono | Computador | Pedidos de placas com a situação (aguardando pagamento, pago, em produção, enviado, entregue) e o link de pagamento |
| Equipe | Dono | Computador | Criar, editar, desativar e excluir vendedor, comissão própria, convite |
| Comissões e Razão | Dono | Computador | Saldo de cada vendedor, repasse com comprovante; razão do mês |
| Configurações | Dono | Computador e celular | Comissão padrão, preço da renovação e o contrato de revenda (reenviar, conferir assinatura, baixar cópia) |
| Marca | Dono | Computador | Logo com recorte (horizontal ou quadrado, arrastar e zoom), cores e nome exibido, com prévia nas cores escolhidas |
| Auditoria | Dono | Computador | Quem mudou o quê, com antes e depois, busca pelo código, filtro pela ação, 20 por página e exportação em JSON |
| Suporte | Dono | Computador e celular | Chamados com a Avalia, com o prazo de resposta |
| Notificações | Todos | Celular e computador | O sino do cabeçalho - pendências da revenda (contrato, recebimento, celular, endereço, distrato) e avisos ao vivo; o vendedor vê os dele (venda paga, comissão repassada, placas com ele) |
| Meus dados | Todos | Celular e computador | No menu da conta - nome de todos; nome da empresa, celular e endereço do dono (o CNPJ não muda); telefone e chave Pix do vendedor; o dono encerra a conta aqui. O e-mail muda pelo Suporte |
| Segurança | Dono e operadora | Celular e computador | Ativar o segundo fator (app autenticador ou código no e-mail), com códigos de recuperação. Desativar manda um código novo ao e-mail cadastrado; o do app e os de recuperação também valem |
| Painel do vendedor | Vendedor | Celular | Vendas do mês, comissão a receber, esperando pagamento e recebida, placas com ele |
| Nova venda | Dono e vendedor | Celular | Negócio no Google, QR da placa, cliente, cobrança, gravação do NFC; ou venda de serviço, sem placa |
| Ficha da placa | Dono e vendedor | Celular | Destino, vencimento, leituras, histórico, troca, renovação |
| Pedidos, Lotes, Pacotes e Cupons | Operadora | Computador | Fila de pedidos (produção, despacho do lote), geração e impressão de lotes, pacotes e cupons |
| Catálogo da Avalia | Operadora | Computador | Cadastrar e editar produto, foto com recorte, preço sugerido, ordem e situação |
| Serviços da operadora | Operadora | Computador | A fila de ordens de serviço - iniciar, entregar, cancelar |
| Revendedores | Operadora | Computador | Revendas, recebedor no Pagar.me, contrato em papel, situação do encerramento |
| Atendimento | Operadora | Computador | Fila de chamados pelo prazo, com o atrasado em vermelho |
| ERP | Operadora | Computador | Envios ao Protheus e reenvio |
| Auditoria da operadora | Operadora | Computador | O que a operadora mudou na plataforma, com o que foi enviado, busca, 20 por página e exportação em JSON |
| Página do QR | Cliente final | Celular | Redireciona ao destino e conta a leitura |

## 6. Regras de negócio

### Venda

- Preço - o revendedor define a tabela dele; a venda nunca fica abaixo dela, e
  acima vai até 5 vezes. Dar de graça é a Cortesia.
- Venda antes do pagamento - a venda nasce com a cobrança pendente; o cliente
  pode pagar depois, e a comissão só conta quando ele paga.
- Destino do QR - avaliação no Google (lugar buscado no Places ou link colado
  do Perfil da Empresa; o link colado vence a busca) ou link livre do cliente.
  Só http e https, com domínio, até 1.000 caracteres, e nunca o endereço de
  outra placa.
- Só vende placa de lote ativado pelo próprio revendedor. Código de outro
  revendedor responde como inexistente: o código está impresso e qualquer um lê.
- Placa consignada a um vendedor e vendida por outro vale para quem vendeu.
- Cliente - empresa, responsável e telefone obrigatórios; CNPJ ou CPF e e-mail
  opcionais na venda, mas sem eles a cobrança não sai. Um cliente por documento
  ou telefone no revendedor: o que já existe é achado e só ganha o que faltava.
- A venda é gravada antes da cobrança. Pagar.me fora do ar não segura o
  vendedor: a venda fica registrada e a cobrança é gerada de novo pela lista.
- Venda do dono não tem comissão: a parte dele é o que sobra.

### Cobrança

- No balcão, Pix (QR na tela) ou cartão de crédito em até 12 vezes. O cartão
  vai do celular direto ao Pagar.me; a plataforma não vê número nem código.
- Uma cobrança só anda para a frente - pendente vira paga, cancelada ou
  falha; paga só vira estornada. Aviso atrasado não desfaz pagamento.
- Cobrança que não chegou ao Pagar.me é reencontrada pelo código; sem pedido
  depois de 15 minutos, vira falha e pode ser refeita.

### Pedido de placas e cupom

- Cupom - da operadora, em percentual ou em reais, com validade e limite de
  usos. O pedido grava o preço do pacote, o cupom e o desconto do momento; o
  custo de cada placa é o que o revendedor pagou de fato, com o desconto.
- Cupom de 100% faz o pedido nascer pago. Pedido cancelado devolve o uso do
  cupom.
- Pagamento pelo link do checkout do Pagar.me, que vale 3 dias e aceita um
  pagamento só. Pedir o link de novo devolve o mesmo enquanto ele valer. Com
  link em aberto, o pedido não é cancelado: o dinheiro ainda pode entrar.
- Pagamento que chega para pedido já pago ou cancelado avisa a operadora para
  devolver.

### Comissão

| Ponto | Regra |
| --- | --- |
| Quem ajusta | Só o dono, na tela Vendedores; toda mudança vai à auditoria |
| Níveis | Padrão do revendedor, exceção por produto, exceção por vendedor; vale a mais específica |
| Base | Valor bruto da venda |
| Forma | Percentual em pontos base (2000 = 20%) |
| Limite | Recusada se deixar o revendedor sem cobrir a taxa da Avalia e a do Pagar.me |
| Mudança | Vale para venda nova; venda feita mantém o percentual gravado |
| Cortesia e renovação | Sem comissão; o dono pode ligar comissão de renovação |
| Estorno | Lançamento negativo no saldo do vendedor, descontado do próximo repasse |
| Repasse | O revendedor faz o Pix ao vendedor e registra na tela, com data de corte opcional e comprovante. O repasse paga a comissão devida até o corte, menos o já repassado. Sem saldo, recusa |
| Saldo | O vendedor vê a comissão a receber e a já recebida em Minhas vendas |

### Razão

- Só inserção: correção é uma linha nova com o valor contrário, que aponta
  para a corrigida, na competência do dia do estorno. Mês fechado não muda.
- Venda paga lança a receita bruta, a taxa da Avalia e a comissão devida;
  renovação paga lança receita e taxa, sem comissão; repasse lança a comissão
  paga. O mesmo pagamento nunca é lançado duas vezes.
- Competência no fuso de Brasília. O líquido do mês é receita menos taxa menos
  comissão; a taxa do Pagar.me sai no extrato do Pagar.me.

### Auditoria

- Fica registrado quem fez, o quê, onde, o valor de antes e o de depois:
  destino da placa (na venda e nas trocas), suspender e pôr no ar, troca de
  placa, cancelamento, cortesia, recebido por fora e o desfazer, equipe
  (cadastro, alteração, desativação, exclusão), comissão padrão, produto,
  repasse, marca, links e anonimização do cliente.
- A ficha da placa mostra o histórico dela; a tela Auditoria, só do dono,
  mostra tudo, com busca pelo código.
- Registro não se altera nem se apaga, nem pela aplicação. A anonimização
  registra o fato sem o nome do cliente.
- A operadora tem a auditoria dela: toda mudança que ela faz na plataforma e
  deu certo fica registrada com quem, quando, a ação, o alvo e o que foi
  enviado. Ação nova da operadora entra sozinha, sem código a mais.

### Link curto

- `/L/{codigo}` - mesmo formato do código da placa (6 caracteres Crockford),
  cerca de 33 bytes; cabe em qualquer tag NFC.
- A venda com destino no Google cria o link do negócio, ou reaproveita o que já
  leva ao mesmo destino. Em Serviços, as abas Link e Maps criam o link do
  negócio sem placa.
- O código nunca volta ao sorteio, nem depois de o link ser excluído.
- Clique conta só no GET de gente; HEAD e robô não contam. Desligado mostra
  aviso; excluído responde não encontrado.
- Destino não pode ser outra placa nem outro link da plataforma.

### Lote e placa

- Código - seis caracteres do alfabeto Crockford base32 (sem I, L, O e U),
  sorteados, únicos na plataforma inteira e nunca reaproveitados. Na leitura,
  I e L viram 1 e O vira 0; minúscula, espaço e hífen são aceitos.
- Código reservado - os códigos que já existem no Sales atual da Avalia entram
  como reservados e o sorteio não os usa.
- Lote - numerado em sequência (LT-0001), de 1 a 1.000 placas, com a sequência
  das placas contínua entre lotes. A impressão é um ZIP com QR em SVG e PNG por
  placa ("0001-CODIGO") e um CSV codigo,photo para a mala direta do Corel. O QR
  grava a URL em maiúsculas, nível H de correção.
- Situação - em estoque, ativa, suspensa ou baixada. Carência e vencida saem
  da data: a placa vale 12 meses da venda e continua no ar por mais 30 dias de
  carência; depois, mostra a página de vencida.
- Placa sem venda não vai ao ar. Lida, abre a Nova venda com o código.
- Leitura - redireciona com 302 e sem cache, para o destino trocado valer na
  hora. Conta por dia no fuso de Brasília; leitura de robô e de pré-visualização
  não conta, e falha na contagem nunca derruba o redirecionamento.
- Consignação - só dentro do revendedor, do estoque dele para um vendedor e de
  volta: a Avalia não consigna ao revendedor, porque o pacote é pago antes. Até
  20 placas por vez; pelos códigos é tudo ou nada, pela quantidade saem as de
  menor sequência. Vendedor excluído devolve o que tinha.
- Suspender não apaga nada: a placa volta exatamente como estava. Só o dono ou
  quem vendeu a placa muda o destino e suspende.
- O lote é ligado ao pedido no despacho; a ativação só aceita lote ligado a
  pedido do próprio revendedor, para um número digitado errado não prender o
  lote de outro.
- Renovação - 1 ano por 60% do valor da venda, sem comissão; o dono ajusta o
  percentual (10% a 100%) e vale para renovação nova. Conta a partir do
  vencimento antigo, mesmo paga com atraso. Uma renovação em aberto por placa;
  pedir de novo devolve o mesmo Pix.
- Troca de placa - a venda inteira vai para o código novo, a antiga fica
  baixada e a perda vai ao razão.
- Vendedor com venda é desativado, e não excluído; sem venda, exclui.

### ERP

- Pedido de placas pago vira pedido de venda no Protheus da Avalia, uma vez
  só. O envio que falhou volta para a fila; depois de 8 tentativas para, e a
  operadora reenvia pela tela ERP.

### Mapa

- O dono vê no mapa do Google os negócios que compraram placa com lugar do
  Google, com placas, placas no ar e leituras dos últimos 30 dias. Sem a chave
  do mapa configurada, a mesma informação vem em lista, com o link para o
  Google Maps.

### Atendimento

- O dono fala com a Avalia em chamados (menu Suporte, tela "Contate o suporte"):
  abre com assunto e mensagem, acompanha a resposta e fecha o chamado.
  Mensagem nova num chamado fechado reabre o chamado. Mensagem não é editada
  nem apagada. Abrir o chamado marca como lidas as mensagens do outro lado, e a
  mensagem nova chega ao vivo.
- Atendimento de segunda a sexta, das 8h às 18h de Brasília, sem feriado
  nacional (os fixos e a Sexta-feira Santa). Prazo de resposta - 24 horas
  contadas só em dia útil, a partir de cada mensagem da revenda que espera a
  Avalia; fora do expediente, conta da abertura seguinte. Exemplo - sexta às
  19h vence terça às 8h. A resposta da Avalia fecha o prazo.
- A operadora atende todos numa fila: o que espera a Avalia primeiro, pelo
  prazo, com o atrasado em vermelho. Chamado novo avisa a operadora por e-mail;
  resposta da Avalia avisa o dono por e-mail.

### Avisos ao vivo

- Venda, pagamento, pedido, estoque, repasse e ERP atualizam as telas abertas
  sem recarregar, com um aviso curto. Cada revendedor só recebe os próprios.

### Contrato

- O contrato do revendedor e o termo do cliente são assinados na D4Sign.
- Em Configurações, o dono vê a situação do contrato, reenvia o e-mail de
  assinatura quando não chegou (um pedido por minuto, como a D4Sign aceita),
  confere na D4Sign se já assinou (Conferir assinatura, uma vez por minuto) e
  baixa uma cópia em PDF - a original antes de assinar, a assinada depois.
- Aviso da D4Sign perdido não deixa o contrato parado - a Plataforma reconsulta
  sozinha quem espera assinatura há mais de 30 minutos. Assinado, o dono recebe
  o aviso no sino; o mesmo vale para o distrato (conta encerrada ou distrato cancelado).
- A Plataforma gera a versão 2 do contrato preenchida com o cadastro - razão
  social, CNPJ, endereço, e-mail e o dono como representante; local e data da
  emissão. A Avalia sai aceita na emissão (representante e cargo da
  configuração) e só a revenda assina na D4Sign, sem testemunhas (assinatura
  eletrônica por provedor, art. 784, § 4º, do CPC). Revenda sem endereço espera
  o endereço para o contrato sair. Quem já recebeu a versão 1 segue com ela.
- Texto em `src/AvaliaSales.Infrastructure/Impressao/Modelos`;
  `node docs/contrato/gerar.mjs` imprime um exemplo para revisão.
- O PDF original e o hash SHA-256 dele ficam guardados, para provar qual texto
  foi assinado.
- Contrato só anda para a frente - aguardando assinatura vira assinado ou
  cancelado.
- O dono só usa o painel depois de confirmar o e-mail: o cadastro manda o link
  e um código de 6 dígitos (valem 2 dias) e, até lá, o painel mostra só a
  confirmação, com campo do código e reenviar depois de 1 minuto. Cinco
  códigos errados encerram o envio; reenviar manda outro.
  Contrato e avisos também só vão para e-mail confirmado. Conta criada pela
  operadora já nasce confirmada. O vendedor não passa por essa trava.
- Entrada de aparelho novo na conta gera um e-mail ao dono da conta, com
  navegador, sistema, horário e IP, e o link para trocar a senha.

### Encerramento da conta (distrato)

- O dono pede em Meus dados (Encerrar conta). A Plataforma gera o distrato com
  os dados dela e manda à D4Sign, como o contrato. Até assinar, ele reenvia o
  e-mail, baixa a cópia ou desiste (o distrato é cancelado na D4Sign).
- Preenchido pela Plataforma - data do contrato; data efetiva igual à da
  assinatura do distrato; exportação por 30 dias depois dela; saldo devido à
  Avalia igual à taxa das vendas e renovações recebidas por fora, com
  vencimento em 10 dias da data efetiva; saldo devido pela Avalia zero (o split
  paga a revenda direto); estoque e lotes; Anexo I com os clientes ativos e
  Anexo II com a conciliação, na posição da emissão.
- Estoque - comprado é da revenda, sem devolução: as placas em estoque têm baixa
  na data efetiva (opção "manterá as placas, mas não poderá ativá-las").
- Assinado o distrato - vendas, pedidos e qualquer escrita param; a equipe perde
  o acesso; as placas no ar (dentro da carência), os clientes delas e os links
  curtos desses clientes passam para a revenda da Avalia (a do CNPJ da Avalia),
  que renova pelo valor da venda antiga. O QR continua respondendo. A venda e o
  razão ficam com a revenda antiga.
- O dono entra só para consultar, exportar os dados (arquivo JSON com clientes,
  vendas, placas e razão) e baixar o distrato, por 30 dias; depois o login é
  desligado. Os registros que a lei exige continuam guardados.
- Revenda que nunca assinou o contrato não tem o que distratar: encerra na hora.
- Conta encerrada mostra a faixa "Conta encerrada" em toda tela e esconde o que
  só serve para vender (Nova venda, Serviços, Catálogo, Marca e Mapa); a busca no
  Google também é recusada.
- Reabertura - o dono pede pela faixa de conta encerrada, com motivo opcional; o
  pedido vira um chamado no suporte e aparece para a operadora em Revendedores,
  que aprova ou recusa. Aprovada, a conta volta, o dono recebe aviso no sino e por
  e-mail, assina um contrato novo antes do próximo pedido e religa a equipe pela
  tela Equipe. O que o distrato fez fica: os clientes transferidos continuam com
  a Avalia e o estoque baixado não volta.

### E-mail

- Sai sempre do endereço da plataforma, o único com domínio verificado.
- O que sai em nome da revenda (convite do vendedor) leva o nome dela no
  remetente ("Padaria via Avalia Sales"), a resposta vai ao dono e o botão
  vem na cor da marca.
- No iPhone, o NFC é gravado pelo app NFC Tools: o botão copia o endereço e
  abre o app.

## 6a. Mapa de portabilidade do Sales

O Sales da Avalia já opera essas regras com a equipe interna. A plataforma
porta o negócio e troca a tecnologia onde a stack pede.

| No Sales hoje | Na plataforma | Situação |
| --- | --- | --- |
| Código Crockford de 6 caracteres e `/q/{codigo}` | Igual, com `/q/` e `/Q/` | No código |
| Lote LT-0001, ZIP com SVG, PNG e CSV para o Corel | Igual, gerado pela operadora | No código |
| Compra de placas da gráfica com custo congelado | Pacote pago pelo revendedor; custo da placa = preço do pacote dividido pelas placas | No código |
| Consignação ao vendedor, 20 por vez, tudo ou nada | Igual, só dentro do revendedor | No código |
| Situação e estado, 12 meses, carência de 30 dias | Igual | No código |
| Contagem de leitura por dia, sem derrubar o redirecionamento | Igual, com soma atômica no Postgres | No código |
| Valor nunca abaixo da tabela, até 5 vezes | Igual | No código |
| Comissão de 25% sobre a venda, sócio sem comissão | Percentual por revendedor e por vendedor; dono sem comissão | No código |
| Venda só na Nova venda, placa sem venda fora do ar | Igual | No código |
| Pix e cartão pelo Asaas | Pix e cartão pelo Pagar.me, com split da taxa da Avalia | Pix no código; cartão pede a tokenização no front |
| Busca do lugar no Google Places e link colado do perfil | Places (New) Text Search pelo servidor e link colado | No código |
| Mapa de bairros em Leaflet com OpenStreetMap | Google Maps JavaScript | No código; espera a chave do navegador |
| Placa coringa de teste (fluxo desfeito no fim) | Lote de teste da revendedora Avalia em homologação | Planejado |
| Renovação por 60% do valor, sem comissão | Igual, com o percentual ajustável pelo dono | No código |
| Confirmação de pagamento em dinheiro ou outro Pix | Igual, depois de o Pix vencer; a taxa da Avalia fica a acertar no razão; o dono desfaz com estorno | No código |
| Cortesia, cancelamento e troca de placa com estorno no razão | Igual, só sem cobrança em aberto ou paga; paga sai por estorno no Pagar.me, que já desfaz o razão | No código |
| Pix e confirmação pelo WhatsApp | E-mail pelo Resend; WhatsApp fora do escopo | No código (e-mail) |

### Depois da venda

- Cancelar e dar de cortesia são do dono, e só com a venda sem cobrança em
  aberto nem paga. O Pix em aberto vale 30 minutos e ainda pode ser pago pelo
  QR; a venda paga sai por estorno no Pagar.me, que já lança o razão ao
  contrário. Cancelada, a placa volta ao estoque e sai do ar. Cortesia mantém
  a placa no ar com valor e comissão zerados.
- Trocar a placa é do dono: a venda, o destino e o vencimento passam para uma
  placa livre do estoque, e a antiga é encerrada e não volta a circular.
- Recebido em dinheiro ou por outro Pix é confirmado pelo vendedor ou pelo
  dono, também só sem Pix em aberto. O razão recebe a venda como se o Pix
  tivesse caído, com a comissão devida, e a taxa da Avalia como "a acertar",
  porque não passou pelo split. Só o dono desfaz, e só antes do repasse da
  comissão. A renovação também se confirma por fora, na ficha da placa.
- Renovação em aberto segura o cancelamento e a troca: paga depois, ela
  estenderia uma placa fora da venda.
- A definir - como a Avalia cobra a taxa das vendas recebidas por fora.

## 6b. Dados pessoais (LGPD)

- Papéis - o revendedor é o controlador dos dados dos clientes e da equipe
  dele; a Avalia, como plataforma, é a operadora. A Avalia é controladora só
  dos dados dos revendedores (cadastro, contrato, pedidos).
- Dado mínimo - do cliente, empresa, responsável e telefone; CNPJ ou CPF e
  e-mail só quando há cobrança, porque o Pagar.me pede. Do lugar no Google,
  só o id.
- Direitos do titular - na tela Clientes, o dono exporta tudo o que a
  plataforma guarda sobre o cliente (dados, vendas, placas, cobranças) em JSON,
  e anonimiza. A anonimização apaga nome, responsável, telefone, documento,
  e-mail e lugar, não volta atrás, e é recusada com cobrança em aberto. Vendas
  e razão ficam, com os valores, pela obrigação fiscal.
- Logs e Sentry sem dado pessoal: nem corpo, nem cookie, nem IP.
- Fim do contrato - pelo distrato, a revenda exporta os dados por 30 dias;
  depois, a Avalia elimina ou anonimiza o que não precisar manter, e guarda o
  que a lei exige (cláusulas 3.3 do distrato e 8.9 do contrato).
- A definir com o jurídico - o texto da política de privacidade.

## 7. Marca do revendedor

O dono configura, na tela Marca, o nome exibido (até 40 caracteres), a cor
primária, a cor de destaque e o logo. Vale para todos os usuários dele no app.
A tela mostra a prévia e o contraste antes de salvar, e a API confere de novo:

- Cor primária - fundo de botão; o texto por cima (branco ou escuro, o de maior
  contraste) precisa de 4,5:1.
- Cor de destaque - anel de foco e realce do menu; precisa de 3:1 sobre o
  branco.
- Logo - qualquer foto ou imagem de até 25 MB; o navegador reduz para 512 px no lado maior e grava em WebP (PNG onde não há WebP). A API guarda até 512 KB, conferido pelos bytes do arquivo.

A tela de login e a área da operadora usam a marca da plataforma. Voltar à
marca da plataforma apaga cores, nome e logo. O e-mail enviado em nome da
revenda leva o nome e a cor dela (seção E-mail).

## 8. Fora do escopo

- Avalia Gestor, Lia, página de links e loja, que seguem no sistema atual da
  Avalia.
- WhatsApp e atendimento por IA. O atendimento é feito nos chamados, dentro da
  plataforma.
- Domínio próprio por revendedor. Todos usam o mesmo domínio.

## 9. A Avalia na plataforma

1. Teste - o fluxo roda em homologação com os sandboxes; depois a revendedora
   Avalia compra 10 placas de si mesma, paga como qualquer revendedor e vende a
   clientes reais. O lote usa uma faixa de códigos que não existe no Sales.
2. Integração - placa nova nasce na plataforma, placa antiga fica no Sales.
   Cada placa tem uma casa só, decidida pela faixa do código.
3. Migração - as placas antigas entram por uma importação que serviria a
   qualquer revendedor, ensaiada em cópia da produção até os totais baterem ao
   centavo.
4. Volta - o Sales fica intacto por semanas; voltar é desligar o repasse.

Ferramentas prontas (docs/OPERACAO.md) - reservar os códigos do Sales antes
de qualquer lote de produção, converter o backup diário do Sales no arquivo de
importação e importar as placas por um arquivo que serve a qualquer revendedor,
com ensaio que só mostra os totais. A venda importada mantém o valor de origem,
sem cobrança, e a comissão devida vira saldo do vendedor.

Regra que não quebra - `avaliaone.com.br/q/{codigo}` responde para sempre,
porque está impresso nas placas vendidas.

## 10. Fases

| Fase | Conteúdo | Situação |
| --- | --- | --- |
| 0. Base | Solução em camadas, login, isolamento entre revendedores, CI e publicação | Em produção |
| 1. Revenda | Cadastro, contrato, pedido, lote, equipe, venda, NFC e QR | Em produção; a cobrança espera a conta do Pagar.me |
| 2. Pagamento | Checkout do pedido de placas, cupom, comissão, razão | Pronta; espera a conta do Pagar.me |
| 3. Serviços | Chamados, avisos ao vivo, mapa, Worker, Protheus simulado, marca da revenda, serviços vendidos | Em produção; o mapa espera a chave do navegador |
| 4. Avalia | Teste, integração com o Sales, migração e volta | Ferramentas prontas (reserva de códigos, conversor do backup, importação com ensaio); falta a virada com o Sales |
| 5. Encerramento | Distrato gerado pela plataforma, transferência dos clientes ativos, exportação | Em produção |

## 11. Decisões pendentes

| Decisão | Opções | Recomendação |
| --- | --- | --- |
| Taxa da Avalia nas vendas recebidas por fora | Cobrar a taxa ou não | O distrato já cobra o saldo a acertar; falta a regra durante o contrato (cláusula 5.6) |
| Renovação de venda dada em cortesia | 60% de zero (R$ 0,01) ou 60% da tabela gravada na venda | A definir |
| Recebedor da revendedora Avalia | Mesmo da conta do marketplace ou separado | Confirmar com o Pagar.me |
| Texto do termo do cliente | Rascunho ou revisão jurídica | Revisão jurídica |
| Política de privacidade | Texto | Revisão jurídica |

### Integrações

| Integração | Situação | Variáveis |
| --- | --- | --- |
| D4Sign | Em produção no ambiente de testes (sem validade jurídica); passar para a conta de produção (`secure.d4sign.com.br`) | `D4Sign__UrlBase`, `D4Sign__TokenApi`, `D4Sign__CryptKey`, `D4Sign__SegredoHmac`, `D4Sign__Cofre` |
| Resend | Em produção, domínio sales.avaliaone.com.br verificado | `Resend__ChaveApi`, `Resend__Remetente`, `Resend__ResponderPara` |
| Google Places | Em produção, chave do servidor restrita ao IP do VPS | `Google__ChaveDoServidor` |
| Google Maps no navegador | Espera a chave restrita ao domínio e o Map ID | `Google__ChaveDoMapa`, `Google__IdDoMapa` |
| BrasilAPI | Em produção, sem chave | - |
| Gotenberg | Em produção, contêiner sem rede | `Pdf__UrlDoConversor` |
| Pagar.me | Espera a conta da Avalia. Ensaiar no ambiente de testes - Pix, cartão (formato do `/tokens` com a chave pública), split e webhook; confirmar a conta com split | `Pagarme__ChaveSecreta`, `Pagarme__ChavePublica`, `Pagarme__RecebedorAvalia`, `Pagarme__SegredoDoWebhook` |
| Recebedor do revendedor | Criar pela API (`POST /recipients` com `register_information` e KYC). A referência de 2026 se contradiz no prefixo do id (`re_` ou `rp_`), nos tipos (texto ou número) e no formato da data; escrever com o ambiente de testes. Enquanto isso, a operadora informa o `rp_` à mão | - |
| Sentry e SonarCloud | Ligam com o DSN e o token; o token do SonarCloud não está configurado | `Sentry__Dsn`, `SONAR_TOKEN` e as variáveis do projeto |

## 12. Registro de mudanças

| Data | Mudança |
| --- | --- |
| 23/10/2026 | Cadastro temporário de operadora na tela de entrada - só e-mail @avaliaone.com.br, com código de 6 dígitos enviado ao e-mail. Fica fechado e só abre enquanto a Avalia cria a conta; cada operadora criada entra na auditoria. A operadora não pertence a revenda e não assina contrato. Sai depois da criação das contas da operação. |
| 23/10/2026 | Importação recusada em conta encerrada, também no ensaio; vale depois da reabertura aprovada. |
| 23/10/2026 | Venda trazida do sistema anterior aparece como "Paga no sistema anterior" e não se cobra, cancela, vira cortesia nem recebe por fora. A comissão devida trazida entra no mês da importação, sem mudar mês fechado do razão. |
| 22/10/2026 | Migração do Sales - conversor do backup diário para o arquivo de placas, venda no produto do catálogo pela chave, comissão devida trazida como saldo do vendedor. Os dois sócios entram como vendedores com 50% cada, em vez de divisão entre sócios. |
| 23/10/2026 | Conta encerrada só consulta também na tela (sem Nova venda, Serviços, Catálogo, Marca e Mapa; sem busca no Google) e mostra a faixa de encerramento. Reabertura pedida ao suporte e aprovada ou recusada pela operadora, com contrato novo. |
| 23/10/2026 | Documento revisado para a produção - situação, telas por área, fases, decisões e integrações atualizadas; repetições removidas. |
| 23/10/2026 | Depois de uma publicação, a aba que já estava aberta recarrega sozinha ao abrir uma tela, em vez de mostrar erro; erro de tela mostra o que fazer, com Recarregar. |
| 23/10/2026 | Desativar o segundo fator pede o código novo enviado ao e-mail cadastrado. Sino também para o vendedor - venda paga, comissão repassada e placas consignadas a ele. O logo do cabeçalho leva ao início. Equipe com as ações numa linha (excluir em ícone). |
| 23/10/2026 | Auditoria da operadora, com tela própria. Seletores de tipo de cupom, forma de pagamento e formato do logo passam de abas para escolha de opção (leitor de tela). |
| 23/10/2026 | Conferir assinatura do contrato e do distrato, reconsulta automática a cada 30 minutos e aviso ao dono no sino. Meus dados - o endereço passa a ser gravado, o aviso de salvo não some e revenda sem celular salva o endereço. |
| 22/10/2026 | Texto revisado - a cláusula 5.4 do contrato perde a nota de confirmar a taxa antes da assinatura; a 1.2 do distrato passa a citar a cláusula 3.3 (exportação). Prazos confirmados pela Avalia - exportação em 30 dias e saldo em 10 dias da data efetiva. |
| 22/10/2026 | Contrato versão 2 gerado com os dados do cadastro, com a Avalia aceita na emissão e sem testemunhas. Endereço no cadastro e em Meus dados, preenchido pelo CNPJ e pelo CEP. Encerramento da conta pelo distrato - gerado e enviado à D4Sign, desistência, baixa do estoque, equipe desligada, clientes ativos para a revenda da Avalia e 30 dias para exportar. |
| 22/10/2026 | Contrato de revenda versão 2 e distrato formatados em `docs/contrato`, fora da Plataforma até os campos variáveis serem preenchidos por ela. |
| 22/10/2026 | Serviços em quatro abas - Link (endereço colado), NFC, Maps (busca do negócio no Google) e Status (ordens de serviço). A busca no Google não recarrega mais a página. |
| 22/10/2026 | Contrato em Configurações com reenviar o e-mail de assinatura e baixar a cópia. Na Nova venda, a barra de baixo aparece, com o botão da venda acima dela. |
| 22/10/2026 | Configurações com a comissão padrão e a renovação; gráficos no Início; pendências da revenda no sino; Links vira Serviços com a aba Acompanhar; convite do vendedor vale 24 horas; Meus dados com um Salvar só; chamado se fecha (Fechar); auditoria abre o antes e o depois num modal; Catálogo na barra de baixo do celular; foto do QR Code Dinâmico Pro. |
| 22/10/2026 | Catálogo da Avalia com os 8 produtos do site, foto e situação; a revenda só define o preço dela, nunca abaixo do custo. Pacotes de 20, 50 e 100 placas com o preço por placa. Pedido com em produção e entregue. Venda de serviço sem placa, que paga vira ordem de serviço para a Avalia. Sino de notificações para o dono e a operadora. |
| 22/10/2026 | Menu da conta no cabeçalho com Meus dados, Segurança e Sair; o celular da revenda passa da tela inicial para Meus dados. Auditoria em tabela, com filtro pela ação, paginação e exportação em JSON. |
| 22/10/2026 | Suporte em chamados com prazo de 24 horas úteis (segunda a sexta, 8h às 18h, sem feriado nacional), fila da operadora pelo prazo e aviso por e-mail. As conversas do chat anterior viram um chamado resolvido por revenda. |
| 22/10/2026 | Contrato de revenda aprovado (versão 1), sem o aviso de minuta. Menu lateral em grupos com ícones; "Pedidos de placas" vira Pedidos e "Falar com a Avalia" vira Suporte. Links em abas Encurtador e NFC, com qualquer endereço colado. Logo com recorte. Avisos do contrato na tela inicial reescritos. |
| 22/10/2026 | Celular da revenda obrigatório no cadastro; o dono troca na tela inicial e a operadora vê na lista de revendedores. |
| 22/10/2026 | Confirmação do e-mail por código digitado na tela, além do link, com reenvio depois de 1 minuto. E-mail recusado pelo provedor tenta de novo por uns 3 dias. Campos com máscara (só números em CNPJ, CPF, telefone e código), contorno no traço da marca e verde quando o dado confere. Logo Avalia Sales completo nas telas de entrada e só o gauge dentro do app. |
| 22/10/2026 | Contrato de revenda formatado, com o logo e os dados da Avalia, em minuta para revisão jurídica. |
| 22/10/2026 | Webhook da D4Sign aceita JSON além de formulário. Chave pública do Pagar.me e endereço de resposta do e-mail passam pelo compose. |
| 22/10/2026 | O dono só usa o painel depois de confirmar o e-mail. Segundo fator também por código no e-mail. Aviso por e-mail de acesso de aparelho novo. Entrada clara com o logo da Avalia. |
| 22/10/2026 | Renovação pedida e não paga (sem Pix vivo) não impede mais cancelar a venda nem trocar a placa: ela é cancelada junto. |
| 22/10/2026 | Venda no cartão de crédito na Nova venda, em até 12 vezes, quando a revenda tem a chave pública do Pagar.me. |
| 22/10/2026 | Segundo fator por app autenticador para o dono e a operadora, opcional. |
| 22/10/2026 | Auditoria - histórico na ficha da placa e tela Auditoria do dono. |
| 22/10/2026 | Comissão por produto no catálogo - vale a do vendedor, depois a do produto, depois a padrão. |
| 22/10/2026 | Celular e computador revistos - tabelas viram cartões no celular, menu lateral no computador, traço e títulos da marca Avalia, entrada na cor noite. |
| 22/10/2026 | Confirmação do e-mail do dono antes do contrato e dos avisos. E-mail da revenda com o nome dela no remetente, resposta ao dono e a cor da marca. No iPhone, NFC pelo NFC Tools. |
| 22/10/2026 | Painel do vendedor (primeira tela dele) - vendas e valor do mês, comissão a receber, esperando o cliente pagar, recebida, placas com ele e últimas vendas. |
| 22/10/2026 | Celular - menu na barra de baixo (telas de todo dia e "Mais"); Nova venda com "‹ Voltar" no topo (um passo para trás; no primeiro, sai) e só o botão de avançar no pé, campo do cliente verde quando o dado confere e link do Google conferido no passo do negócio. |
| 22/10/2026 | Nova venda começa pelo negócio no Google, depois a placa. Link curto `/L/` do negócio, criado na venda com Google e na tela Links. |
| 22/10/2026 | Vendedor convidado por e-mail cria a própria senha; o dono também pode mandar o link pelo WhatsApp. Esqueci a senha na entrada. |
| 22/10/2026 | Texto das telas revisto - sem frase de explicação; rótulo, ação e recusa curta. |
| 22/10/2026 | Renovação recebida por fora. Logo aceita foto grande e é reduzido no navegador. |
| 22/10/2026 | Depois da venda - cancelamento, cortesia, troca de placa e recebido em dinheiro ou outro Pix, na tela Vendas. |
| 21/10/2026 | LGPD - papéis de controlador e operadora, dado mínimo, exportação e anonimização do cliente na tela Clientes. |
| 21/10/2026 | Fase 4, ferramentas - reserva de códigos de outro sistema e importação de placas tudo ou nada, com ensaio e totais. |
| 21/10/2026 | Preparada a publicação em servidor próprio (manual em OPERACAO.md); a regra de que `/q/` responde para sempre vale para o deploy, que troca a API em segundos. |
| 21/10/2026 | Fase 3 fechada - mapa dos lugares vendidos (lista sem a chave do Google) e chat entre revendedor e Avalia. |
| 21/10/2026 | White label - tela Marca com nome exibido, cores e logo, contraste conferido na tela e na API. |
| 21/10/2026 | Avisos ao vivo nas telas e pedido de venda no Protheus para cada pedido de placas pago. |
| 21/10/2026 | Fase 2 - pedido de placas pago pelo checkout do Pagar.me, cupons da operadora, razão do revendedor, saldo e repasse de comissão, renovação da placa. |
| 21/10/2026 | Telas da Fase 1 para a operadora, o dono e o vendedor. Recarregar a página mantém o login. |
| 21/10/2026 | Fase 1 na API - cadastro pela tela, contrato automático, pacotes, pedido, despacho e ativação de lote, estoque, consignação, equipe, venda com cobrança, ficha da placa e leitura do QR. Regras portadas do Sales, com o mapa de portabilidade (seção 6a). Papéis operadora, dono e vendedor. |
| 21/10/2026 | Consignação só entre o revendedor e o vendedor dele; a Avalia não consigna ao revendedor. Mapa dos lugares vendidos no Google Maps. |
| 21/10/2026 | Primeiras telas: entrada e catálogo do revendedor (listar e cadastrar produto com preço em reais). |
| 21/10/2026 | Documento criado a partir do desenho técnico. Inclui o modelo de cobrança com taxa gravada na cobrança, a regra de que cobrança e contrato só andam para a frente, e a retomada de cobrança sem pedido. |
