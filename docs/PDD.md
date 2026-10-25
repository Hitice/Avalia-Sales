# Avalia Sales - documento de produto (PDD)

O que a plataforma faz, para quem, e as regras de negócio. A construção fica
no [SDD](SDD.md). Toda mudança de regra atualiza este documento no mesmo
commit, com uma linha no registro de mudanças do fim.

Situação em 25/10/2026 - em produção em sales.avaliaone.com.br, versão 2.7.0.
A cobrança pelo Pagar.me roda em produção desde 23/10/2026, e as placas da
Avalia leem pela plataforma desde 24/10/2026. O contrato pela D4Sign ainda
usa o ambiente de testes, e o pedido de venda ao Protheus espera o serviço do
Protheus (seção 12).

## 1. Visão

Plataforma para revender placas com QR Code e NFC. A placa leva o cliente a
uma avaliação no Google, a um link ou a uma página de links. A Avalia opera a
plataforma, vende as placas às revendas e executa os serviços vendidos. Cada
revenda vende com a própria equipe, recebe pelo Pagar.me e acompanha as
vendas num painel.

## 2. Papéis

| Papel | Quem é | O que faz |
| --- | --- | --- |
| Operadora | A Avalia, como operadora da plataforma | Vende placas às revendas, produz e despacha lotes, mantém o catálogo, executa os serviços vendidos, atende os chamados |
| Revendedor | O dono da revenda | Compra placas, monta a equipe, define preço e comissão, acompanha vendas, estoque, mapa, comissões e financeiro |
| Vendedor | Equipe da revenda | Vende no balcão pelo celular: escolhe os itens, lê o QR da placa, cobra e grava o NFC |
| Cliente final | Quem compra a placa ou o serviço | Paga por Pix ou cartão e usa a placa no negócio |

Termos usados neste documento:

- Revenda - a empresa que revende. É o tenant da plataforma.
- Revendedor - o papel do dono da revenda. A interface mostra "Revendedor";
  o valor interno do papel é `Dono`.

A plataforma tem uma operadora só. A Avalia também é uma revenda, com o mesmo
peso de qualquer outra: mesmo fluxo, mesma taxa, usuários separados dos da
operadora. Nenhuma regra pergunta se a revenda é a Avalia.

## 3. Modelo de negócio

| Dinheiro | Quem paga | Quem recebe | Quando |
| --- | --- | --- | --- |
| Pedido de placas | Revenda | Avalia | Antes do envio do lote |
| Venda da placa | Cliente final | A revenda, pelo split do Pagar.me | No pagamento |
| Venda de serviço | Cliente final | A Avalia a parte dela e a revenda o resto, pelo split do Pagar.me | No pagamento |
| Renovação | Cliente final | A revenda, sem comissão | No pagamento |
| Comissão | Revenda | Vendedor, por Pix, registrado no painel | Quando o revendedor repassa |

- Catálogo - a Avalia cadastra os produtos (placa, placa com serviço e
  serviço) com foto, preço sugerido, pedido mínimo e situação (disponível,
  esgotado, fora de linha). A revenda não cria produto: define o preço dela,
  livre, nunca abaixo do custo. Na placa, o custo é o que a revenda pagou por
  ela no pacote; sem estoque, o preço por placa do pacote mais caro. No
  serviço, o piso é a parte da Avalia (mais o custo da placa, no produto que
  leva placa). Só a placa é comprada no atacado.
- Pacotes de placas - configuráveis pela operadora. Hoje, 20, 50 e 100 placas
  a R$ 23,90, R$ 16,90 e R$ 13,90 a placa (R$ 478,00, R$ 845,00 e
  R$ 1.390,00), e um de 10 para o teste da revenda Avalia. O preço do pacote
  cobre a produção e a margem da Avalia.
- Parte da Avalia no serviço - a revenda compra o serviço com desconto sobre o
  preço sugerido (20% em todos), e a Avalia recebe o resto do sugerido pelo
  split, gravado na venda. Exemplo - landing page sugerida a R$ 899,00:
  R$ 719,20 para a Avalia; a revenda que vende a R$ 899,00 fica com
  R$ 179,80. A operadora muda o desconto por produto no Catálogo da Avalia. A
  placa avulsa não tem parte: a revenda já pagou no pedido de placas.
- Taxa da Avalia - zero. A Avalia ganha nas placas e nos serviços, e a revenda
  paga a tarifa do Pagar.me. A taxa continua um campo por revenda, gravado na
  cobrança no momento dela, para voltar a ser cobrada sem mudar o sistema
  (contrato, cláusulas 5.4 e 5.7). Venda feita guarda a taxa dela. O razão e
  as Configurações só mostram a taxa quando ela existe.
- Serviço vendido - a venda paga de serviço (Perfil no Google, cardápio,
  landing page, sistema, kit mesa, QR dinâmico) vira ordem de serviço para a
  Avalia executar (seção 6.8).

## 4. Jornada da revenda

1. Cadastro - pela tela, com CNPJ, empresa, celular, endereço e o login do
   revendedor. O CNPJ válido preenche a razão social e o endereço (BrasilAPI,
   com os dados da Receita), e o CEP preenche rua, bairro, cidade e UF. O
   revendedor só confere. O recebedor no Pagar.me é informado pela operadora
   até o cadastro de recebedor pela API entrar.
2. Contrato - criado sozinho depois do cadastro e enviado pela D4Sign ao
   e-mail do revendedor. Sem contrato assinado, não há pedido.
3. Pedido - "Pedir placas" no Catálogo ou em Pedidos: pacote e cupom
   opcional, pago antes do envio pelo checkout do Pagar.me.
4. Envio - a Avalia recebe o pedido por e-mail, no sino e na fila, marca em
   produção e despacha um lote da mesma quantidade, pelo envio ou para
   retirada na Avalia. O revendedor recebe o e-mail e a notificação com o
   número do lote.
5. Ativação - o revendedor digita o número do lote (LT-0001, lt 1 ou 1). As
   placas entram no estoque com o custo do pacote, e o pedido fica entregue.
6. Equipe - cadastro dos vendedores, da comissão e da meta de cada um.
7. Venda - o vendedor monta a venda por itens. A placa abre a leitura do QR e
   o destino (negócio buscado no Google, link de avaliação colado ou link do
   cliente); o serviço entra com a quantidade. Depois preenche o cliente e
   cobra tudo numa vez, por Pix ou cartão. No fim grava na tag NFC de cada
   placa o mesmo endereço do QR (`/Q/{codigo}`).
8. Pagamento - o cliente paga e o split divide na hora.
9. Acompanhamento - vendas ao vivo, saldo de comissão, carteira de
   renovações, financeiro do mês e mapa dos lugares vendidos.
10. Encerramento - o revendedor pode encerrar a conta pelo distrato
    (seção 6.13).

## 5. Telas

Todas no mesmo app, liberadas pelo papel do usuário. Toda tabela rola dentro
dela, com o cabeçalho fixo, e a partir de sete linhas ganha uma busca pelo
texto da linha, sem acento. Gravar confirma na tela.

### Acesso e conta

| Tela | Quem usa | O que faz |
| --- | --- | --- |
| Entrada, cadastro e senha | Todos | Entrar, cadastrar a revenda (o CNPJ preenche razão social e endereço), confirmar o e-mail, criar e recuperar a senha. Senha com o botão de mostrar. No pé da entrada, os logos do Pagar.me e da Stone |
| Meus dados | Todos | No menu da conta. Nome de todos; nome da empresa, celular e endereço da revenda (o CNPJ é o do contrato e não muda); telefone e chave Pix do vendedor; troca de senha. O revendedor encerra a conta aqui. O e-mail muda pelo Suporte |
| Segurança | Revendedor e operadora | Segundo fator por app autenticador ou por código no e-mail, com códigos de recuperação. A tela convida a ligar o fator |
| Notificações | Todos | O sino do cabeçalho. Pendências da revenda (contrato, recebimento, celular, endereço, distrato) e avisos ao vivo; o vendedor vê os dele (venda paga, venda cancelada, comissão repassada, placas com ele) |

### Revenda

| Tela | Quem usa | Pensada para | O que faz |
| --- | --- | --- | --- |
| Início do revendedor | Revendedor | Computador e celular | Quatro números do mês (placas em estoque, placas no ar, vendas e recebido), vendas por dia nos últimos 30 dias, para onde foi o dinheiro do mês (resultado, custo das placas, comissões e taxas) e as vendas de hoje com a situação |
| Início do vendedor | Vendedor | Celular | Vendas do mês, comissão a receber, esperando pagamento e recebida, placas com ele; meta do mês com a barra de progresso, ranking da equipe, vendas dos últimos 30 dias e a rosca da comissão |
| Nova venda | Revendedor e vendedor | Celular | Itens (a placa abre a leitura do QR e o destino; o serviço entra com a quantidade, a partir do pedido mínimo), cliente, cobrança de tudo numa vez e gravação do NFC de cada placa |
| Vendas | Revendedor e vendedor | Computador e celular | Lista das vendas, com a quantidade do serviço ("10x Kit Mesa") e as vendas feitas juntas marcadas. Ações - gerar cobrança, receber por fora, trocar a placa, corrigir valor e vendedor, cortesia e cancelamento. O vendedor vê só as dele |
| Detalhe da venda | Revendedor e vendedor | Computador e celular | Aberto pelo nome do cliente na lista. Produto, placa, valor, vendedor, comissão (e se já foi repassada), vencimento, cancelamento com motivo; o cliente; o pagamento (situação, meio, valor, estornado, datas); os itens da mesma cobrança; as ordens de serviço; o Pix em aberto, para copiar ou mandar pelo WhatsApp; as linhas do razão, só para o revendedor. As ações são as da lista. O vendedor só abre a venda dele |
| Ficha da placa | Revendedor e vendedor | Celular | Destino, vencimento, leituras dos últimos 30 dias, histórico, troca e renovação |
| Carteira | Revendedor e vendedor | Celular e computador | Placas vendidas com o vencimento e as leituras dos últimos 30 dias, com os botões Renovar e WhatsApp (mensagem pronta). O vendedor vê a dele; o revendedor, a da revenda inteira |
| Catálogo | Revendedor e vendedor | Computador e celular | Produtos da Avalia com foto, preço sugerido, pedido mínimo e o preço da revenda. O revendedor ajusta o preço e pede placas; Vender abre a Nova venda com o item. O vendedor não vê o custo |
| Clientes | Revendedor | Computador | Clientes da revenda, cadastro antes da venda e a ficha do cliente - editar (com o lugar no Google, que põe no mapa), placas e vendas, juntar cadastro duplicado, excluir o que não tem venda, exportar e anonimizar (LGPD). A coluna Placas conta só placas em venda ativa |
| Serviços | Revendedor e vendedor | Celular | Abas Link (link curto `/L/` de qualquer endereço colado), NFC (grava o link curto na tag), Maps (busca o negócio no Google e cria o link curto da avaliação) e Status (só o revendedor; as ordens dos serviços vendidos). O revendedor edita, desliga e exclui o link |
| Mapa | Revendedor | Computador | Lugares vendidos no Google Maps, em tema escuro, com uma aba por cidade, aberto na que tem mais lugares |
| Estoque | Revendedor (Minhas placas, para o vendedor) | Computador | Placas por situação, com filtro por vendedor; consignação ao vendedor; ativação, lista e arquivamento de lote; baixa de placa com motivo |
| Pedidos | Revendedor | Computador | Pedidos de placas com a situação (aguardando pagamento, pago, em produção, enviado ou pronto para retirada, entregue) e o link de pagamento |
| Equipe | Revendedor | Computador | Criar, editar, desativar e excluir vendedor, com CPF, comissão própria e meta do mês; convite por e-mail ou WhatsApp |
| Comissões | Revendedor | Computador | Saldo de cada vendedor, repasse com comprovante (o botão aparece desligado, com o motivo, quando não há o que pagar) e o desfazer do último repasse |
| Razão | Revendedor | Computador | Linhas do mês, com a coluna Condição para o estorno |
| Financeiro | Revendedor | Celular e computador | Abas Resultado (DRE do mês, gráfico de para onde foi o dinheiro, despesas por categoria, recebido e resultado nos últimos 6 meses), Despesas e Sócios (sócios do contrato e retiradas), com criar, editar e excluir; fechar e reabrir o mês |
| Configurações | Revendedor | Computador e celular | Comissão padrão, percentual da renovação (só leitura; quem define é a operadora) e o contrato de revenda (reenviar, conferir assinatura, baixar cópia) |
| Marca | Revendedor | Computador | Logo com recorte (horizontal ou quadrado, arrastar e zoom), cores, nome exibido e cabeçalho branco ou na cor primária, com prévia nas cores escolhidas |
| Auditoria | Revendedor | Computador | Quem mudou o quê, com antes e depois; busca por placa, cliente, vendedor ou ação; filtro pela ação, 20 por página e exportação em JSON |
| Suporte | Revendedor | Computador e celular | Chamados com a Avalia, com o prazo de resposta |

### Operadora

| Tela | O que faz |
| --- | --- |
| Pedidos | Fila de pedidos de placas - produção e despacho do lote pelo envio ou para retirada |
| Lotes | Geração e impressão de lotes com título e categoria, exclusão antes do primeiro download e arquivamento |
| Pacotes e Cupons | Cadastro dos pacotes de placas e dos cupons |
| Catálogo da Avalia | Cadastrar e editar produto, foto com recorte, preço sugerido, desconto da revenda (e a parte da Avalia que sai dele), pedido mínimo, ordem e situação |
| Serviços | Fila de ordens de serviço - iniciar, entregar, cancelar e reabrir |
| Revendedores | Revendas, recebedor no Pagar.me, percentual da renovação, contrato em papel, suspensão, encerramento e pedido de reabertura |
| Atendimento | Fila de chamados pelo prazo, com o atrasado em vermelho |
| ERP | Envios ao Protheus e reenvio |
| Auditoria da operadora | O que a operadora mudou na plataforma, com o que foi enviado, busca, 20 por página e exportação em JSON |

### Público

| Tela | O que faz |
| --- | --- |
| Página do QR (`/q/{codigo}`) | Redireciona ao destino e conta a leitura; placa vencida, suspensa ou baixada mostra uma página que explica |
| Link curto (`/l/{codigo}`) | Redireciona ao destino e conta o clique |

## 6. Regras de negócio

### 6.1 Acesso e conta

- O revendedor só usa o painel depois de confirmar o e-mail. O cadastro manda
  o link e um código de 6 dígitos, que valem 2 dias. Até a confirmação, o
  painel mostra só a confirmação, com o campo do código e o reenvio depois de
  1 minuto. Cinco códigos errados encerram o envio; reenviar manda outro.
- Contrato e avisos só vão para e-mail confirmado. Conta criada pela
  operadora já nasce confirmada. O vendedor não passa por essa trava.
- Entrada de aparelho novo gera um e-mail ao dono da conta, com navegador,
  sistema, horário e IP, e o link para trocar a senha. O primeiro aparelho
  entra sem aviso.
- Troca de senha logada, para todos os papéis - pede a senha atual, encerra
  os outros aparelhos e avisa por e-mail. Vale também com a conta encerrada.
- Segundo fator opcional para o revendedor e a operadora. Desativar pede um
  código novo enviado ao e-mail cadastrado; o código do app e os de
  recuperação também valem.
- Vendedor convidado por e-mail cria a própria senha; o convite vale 24
  horas. O revendedor também pode mandar o link pelo WhatsApp.

### 6.2 Venda

- Preço - a revenda define a tabela dela. A venda nunca fica abaixo da
  tabela, e acima vai até 5 vezes. Dar de graça é a cortesia.
- Itens - uma venda leva até 20 itens para o mesmo cliente. Cada placa ou
  serviço é uma venda, com a comissão dela, e juntas formam um grupo com uma
  cobrança só. O valor de cada linha pode ser ajustado. A placa sai uma por
  código.
- Pedido mínimo - o serviço vende por quantidade, nunca abaixo do pedido
  mínimo do catálogo (kit mesa e placa de cardápio a partir de 10). A ordem de
  serviço leva a quantidade.
- Venda antes do pagamento - a venda nasce com a cobrança pendente. O cliente
  pode pagar depois, e a comissão só conta quando ele paga.
- A venda é gravada antes da cobrança. Pagar.me fora do ar não segura o
  vendedor: a venda fica registrada e a cobrança é gerada de novo pela lista.
- Teste gratuito de 7 dias - escolhido na cobrança da Nova venda, só quando a
  venda tem apenas placas. Não há cobrança na hora. Dois dias antes do fim, o
  revendedor e o vendedor são avisados. No fim, sem pagamento, a placa sai do
  ar e a venda mostra "teste vencido: receber ou devolver". Pago, a placa
  volta ao ar e os 12 meses contam do pagamento.
- Destino do QR - avaliação no Google (lugar buscado no Places ou link colado
  do Perfil da Empresa; o link colado vence a busca) ou link livre do cliente.
  Só http e https, com domínio, até 1.000 caracteres, e nunca o endereço de
  outra placa ou de outro link da plataforma.
- Só vende placa de lote ativado pela própria revenda. Código de outra revenda
  responde como inexistente, porque o código está impresso e qualquer um lê.
- Placa consignada a um vendedor e vendida por outro vale para quem vendeu.
- Cliente - empresa, responsável e telefone obrigatórios. CNPJ ou CPF e
  e-mail são opcionais na venda, mas sem eles a cobrança não sai. Um cliente
  por documento ou telefone na revenda: o que já existe é achado e só ganha o
  que faltava.
- Telefone - gravado sem o 55 do país e sem o 0 da operadora: "+55 34 9…" e
  "34 9…" são o mesmo cliente.
- Venda do revendedor não tem comissão: a parte dele é o que sobra.
- Correção - antes do repasse da comissão, o revendedor corrige a venda. O
  valor muda só sem cobrança viva nem paga. O vendedor muda também na venda
  paga, com a comissão estornada de um e devida ao outro.
- Venda trazida do sistema anterior - aparece como "Paga no sistema anterior"
  e não se cobra, cancela, vira cortesia nem recebe por fora. A troca de placa
  vale. A comissão devida trazida entra no mês da importação, sem mudar mês
  fechado.

### 6.3 Depois da venda

- Cancelamento - do revendedor, com motivo (erro de cadastro, não pagou,
  desistência, defeito, outro), observação e o destino da placa (volta ao
  estoque, voltou com defeito, não voltou). A venda se cancela antes do
  estorno: motivo ou observação inválidos recusam antes de o dinheiro voltar.
- Cobrança no cancelamento - o Pix em aberto é cancelado no Pagar.me, o pago
  volta inteiro ao cliente pelo Pagar.me e o recebido por fora é desfeito. O
  estorno vai ao razão na hora, inclusive o da comissão, que desconta no
  próximo repasse se já foi repassada.
- Placa no cancelamento - sai do ar e volta ao estoque. Se voltou com defeito
  ou não voltou, sai de circulação e a perda vai ao razão.
- Cancelamento dentro de um grupo - na venda paga, devolve só a parte dela
  (estorno parcial no Pagar.me, ou por fora quando foi recebida por fora).
  Com o Pix do grupo em aberto, o Pix cai inteiro e o resto ganha cobrança
  nova em Vendas.
- Estorno recusado pelo Pagar.me (por exemplo, sem saldo na conta para
  devolver o Pix) aparece como recusa no cancelamento, e nada muda: a venda
  segue paga. Nesse caso o estorno sai pelo painel do Pagar.me, que devolve a
  tarifa junto, e depois o cancelamento aqui fecha a venda sem pedir outro
  estorno.
- Efeitos de todo estorno, venha do cancelamento ou do painel do Pagar.me -
  estorna o razão, cancela a ordem de serviço ainda não entregue, avisa a
  operadora da ordem já entregue (para tirar do ar o que foi entregue) e
  encerra no sino o que a venda abriu para o revendedor, o vendedor e a
  operadora. Estorno feito só no painel avisa o revendedor para cancelar a
  venda. O vendedor recebe o aviso da venda dele cancelada, com a comissão
  estornada.
- Cortesia - do revendedor, só com a venda sem cobrança em aberto nem paga.
  Mantém a placa no ar com valor e comissão zerados, e o custo da placa vai ao
  razão no mês da cortesia.
- Troca de placa - com motivo (defeito de impressão, quebrada, perdida). A
  venda, o destino e o vencimento passam para uma placa livre do estoque. A
  antiga fica encerrada com o motivo, guarda a venda de onde saiu e não volta
  a circular. Com o motivo quebrada ou perdida, a perda vai ao razão. O
  vendedor troca na venda dele, com placa que está com ele; o revendedor, com
  qualquer placa livre. Reimpressão de placa com defeito sai
  num lote novo, com o título dizendo de que lote é.
- Recebido por fora - pagamento em dinheiro ou por outro Pix, confirmado pelo
  vendedor ou pelo revendedor, só sem Pix em aberto e sem pagamento. O razão
  recebe a venda como se o Pix tivesse caído, com a comissão devida. O que
  seria da Avalia pelo split (taxa e parte do serviço) fica "a acertar"; com
  a taxa zero, a placa recebida por fora não deixa nada a acertar. Só o
  revendedor desfaz, e só antes do repasse da comissão. A renovação também se
  confirma por fora, na ficha da placa.
- Renovação em aberto com Pix vivo segura o cancelamento e a troca, porque,
  paga depois, estenderia uma placa fora da venda. Renovação sem Pix vivo é
  cancelada junto.

### 6.4 Pedido de placas e cupom

- Pagamento pelo checkout do Pagar.me (Pix, cartão ou boleto, tudo para a
  Avalia). O link vale 3 dias e aceita um pagamento só. Pedir o link de novo
  devolve o mesmo enquanto ele valer. Com link em aberto, o pedido não é
  cancelado, porque o dinheiro ainda pode entrar.
- A operadora também pode registrar um pagamento recebido por fora.
- Situação do pedido - aguardando pagamento, pago, em produção, enviado ou
  pronto para retirada, entregue, ou cancelado.
- Retirada - a operadora escolhe "Pronto para retirada" em vez de despachar;
  o revendedor recebe o aviso e ativa o lote do mesmo jeito.
- Cupom - da operadora, em percentual ou em reais, com validade e limite de
  usos. O pedido grava o preço do pacote, o cupom e o desconto do momento.
- Cupom de 100% faz o pedido nascer pago. Pedido cancelado devolve o uso do
  cupom.
- Pagamento que chega para pedido já pago ou cancelado avisa a operadora para
  devolver.
- Pedido de placas pago vira pedido de venda no Protheus da Avalia, uma vez
  só. O envio que falhou volta para a fila; depois de 8 tentativas para, e a
  operadora reenvia pela tela ERP.

### 6.5 Lote e placa

- Código - seis caracteres do alfabeto Crockford base32 (sem I, L, O e U),
  sorteados, únicos na plataforma inteira e nunca reaproveitados. Na leitura,
  I e L viram 1 e O vira 0; minúscula, espaço e hífen são aceitos.
- Código reservado - os códigos que existem fora da plataforma (os do sistema
  anterior da Avalia) entram como reservados, e o sorteio não os usa.
- Lote - numerado em sequência (LT-0001), de 1 a 1.000 placas, com a
  sequência das placas contínua entre lotes, título e categoria (placa,
  crachá, cardápio, teste, avulso).
- Impressão - ZIP com o QR em SVG e PNG por placa ("0001-CODIGO"), só SVG ou
  só PNG, e um CSV `codigo,photo` para a mala direta do Corel. A arte segue a
  da gráfica: caminho único, 30 mm, fundo transparente, miolo vazado e a marca
  pequena, branca e chapada. O QR grava a URL em maiúsculas, com nível H de
  correção.
- Exclusão e arquivamento - o lote só se exclui antes do primeiro download e
  sem despacho; os códigos dele ficam reservados. Depois, só se arquiva: a
  operadora arquiva o lote livre, e o revendedor, o lote que já está no
  estoque dele. As placas em estoque saem com o motivo "lote arquivado", e
  desarquivar devolve.
- Despacho e ativação - o lote é ligado ao pedido no despacho. A ativação só
  aceita lote ligado a pedido da própria revenda, para um número digitado
  errado não prender o lote de outra.
- Situação - em estoque, ativa, suspensa ou baixada. Carência e vencida saem
  da data: a placa vale 12 meses da venda e continua no ar por mais 30 dias de
  carência; depois, mostra a página de vencida.
- Placa sem venda não vai ao ar. Lida, abre a Nova venda com o código.
- Leitura - redireciona com 302 e sem cache, para o destino trocado valer na
  hora. Conta por dia no fuso de Brasília. Leitura de robô e de
  pré-visualização não conta, e falha na contagem nunca derruba o
  redirecionamento.
- Consignação - só dentro da revenda, do estoque para um vendedor e de volta.
  A Avalia não consigna à revenda, porque o pacote é pago antes. Até 20 placas
  por vez; pelos códigos é tudo ou nada, pela quantidade saem as de menor
  sequência. Vendedor excluído devolve o que tinha.
- Suspender não apaga nada: a placa volta exatamente como estava. Só o
  revendedor ou quem vendeu a placa muda o destino e suspende.
- Baixa - placa sem venda sai do estoque com motivo (defeito de impressão,
  quebrada, perdida, não impressa), e o revendedor desfaz.
- Renovação - 1 ano por um percentual do valor da venda, 60% por padrão, sem
  comissão. A operadora define o percentual de cada revenda (de 10% a 100%)
  em Revendedores, e ele vale para renovação nova; a revenda só lê.
- Base da renovação - o valor da venda. Na cortesia e na venda de R$ 0, a
  tabela gravada na venda; sem ela, o preço de hoje do produto.
- A renovação conta a partir do vencimento antigo, mesmo paga com atraso. Uma
  renovação em aberto por placa; pedir de novo devolve o mesmo Pix. Renova
  quem vendeu a placa ou o revendedor.

### 6.6 Link curto

- `/L/{codigo}` - mesmo formato do código da placa (6 caracteres Crockford),
  cerca de 33 bytes, e cabe em qualquer tag NFC.
- A venda com destino no Google cria o link do negócio, ou reaproveita o que
  já leva ao mesmo destino. Em Serviços, as abas Link e Maps criam o link sem
  placa.
- O código nunca volta ao sorteio, nem depois de o link ser excluído.
- Clique conta só no GET de gente; HEAD e robô não contam. Link desligado
  mostra aviso; excluído responde não encontrado.
- Destino não pode ser outra placa nem outro link da plataforma.

### 6.7 Equipe

- Vendedor com venda é desativado, e não excluído; sem venda, exclui.
- Meta do mês - definida pelo revendedor na Equipe, por vendedor. O vendedor
  vê a barra de progresso no início, pelo vendido pago no mês de Brasília.
- Ranking - posição da equipe no mês por vendas pagas, sem valor nem comissão
  dos outros.

### 6.8 Serviços vendidos

- A venda paga de serviço vira ordem de serviço para a Avalia, com a
  quantidade e a situação a fazer, em andamento, entregue ou cancelada. O
  revendedor acompanha na aba Status de Serviços.
- Ordem entregue ou cancelada por engano se reabre. A cancelada por estorno,
  não.

### 6.9 Mapa

- O revendedor vê no mapa do Google os negócios que compraram placa com lugar
  do Google, com placas, placas no ar e leituras dos últimos 30 dias, separados
  por cidade. Sem a chave do mapa configurada, a mesma informação vem em
  lista, com o link para o Google Maps.

### 6.10 Atendimento e avisos

- O revendedor fala com a Avalia em chamados (menu Suporte, tela "Contate o
  suporte"): abre com assunto e mensagem, acompanha a resposta e fecha o
  chamado. Mensagem nova num chamado fechado reabre o chamado. Mensagem não é editada nem apagada. Abrir
  o chamado marca como lidas as mensagens do outro lado, e a mensagem nova
  chega ao vivo.
- Expediente - segunda a sexta, das 8h às 18h de Brasília, sem feriado
  nacional (os fixos e a Sexta-feira Santa). Prazo de resposta - 24 horas
  contadas só em dia útil, a partir de cada mensagem da revenda que espera a
  Avalia; fora do expediente, conta da abertura seguinte. Exemplo - sexta às
  19h vence terça às 8h. A resposta da Avalia fecha o prazo.
- A operadora atende todos numa fila: o que espera a Avalia primeiro, pelo
  prazo, com o atrasado em vermelho. Chamado novo avisa a operadora por
  e-mail; resposta da Avalia avisa o revendedor por e-mail.
- Avisos ao vivo - venda, pagamento, pedido, estoque, repasse e ERP atualizam
  as telas abertas sem recarregar, com um aviso curto. Cada revenda só recebe
  os próprios.

### 6.11 E-mail e NFC

- O e-mail sai sempre do endereço da plataforma, o único com domínio
  verificado.
- O que sai em nome da revenda (convite do vendedor) leva o nome dela no
  remetente ("Padaria via Avalia Sales"), a resposta vai ao revendedor e o
  botão vem na cor da marca.
- No iPhone, o NFC é gravado pelo app NFC Tools: o botão copia o endereço e
  abre o app. No Android, a gravação é pelo Chrome.

### 6.12 Contrato

- O contrato da revenda é assinado na D4Sign. Contrato assinado em papel é
  registrado pela operadora.
- A plataforma gera a versão 2 do contrato preenchida com o cadastro - razão
  social, CNPJ, endereço, e-mail e o revendedor como representante; local e
  data da emissão. A Avalia sai aceita na emissão (representante e cargo da
  configuração), e só a revenda assina na D4Sign, sem testemunhas (assinatura
  eletrônica por provedor, art. 784, § 4º, do CPC). Revenda sem endereço
  espera o endereço para o contrato sair. Quem já recebeu a versão 1 segue com
  ela.
- Texto em `src/AvaliaSales.Infrastructure/Impressao/Modelos`;
  `node docs/contrato/gerar.mjs` imprime um exemplo para revisão.
- O PDF original e o hash SHA-256 dele ficam guardados, para provar qual texto
  foi assinado.
- O contrato só anda para a frente - aguardando assinatura vira assinado ou
  cancelado.
- Em Configurações, o revendedor vê a situação do contrato, reenvia o e-mail
  de assinatura (um pedido por minuto, como a D4Sign aceita), confere na
  D4Sign se já assinou (Conferir assinatura, uma vez por minuto) e baixa uma
  cópia em PDF - a original antes de assinar, a assinada depois.
- Aviso da D4Sign perdido não deixa o contrato parado: a plataforma reconsulta
  sozinha quem espera assinatura há mais de 30 minutos. Assinado, o
  revendedor recebe o aviso no sino. O mesmo vale para o distrato (conta
  encerrada ou distrato cancelado).
- O termo do cliente final ainda não sai na venda: espera o texto jurídico
  (seção 13).

### 6.13 Encerramento da conta (distrato)

- O revendedor pede em Meus dados (Encerrar conta). A plataforma gera o
  distrato com os dados da revenda e manda à D4Sign, como o contrato. Até
  assinar, ele reenvia o e-mail, baixa a cópia ou desiste (o distrato é
  cancelado na D4Sign).
- Preenchido pela plataforma - data do contrato; data efetiva igual à da
  assinatura do distrato; exportação por 30 dias depois dela; saldo devido à
  Avalia igual à taxa das vendas e renovações recebidas por fora, com
  vencimento em 10 dias da data efetiva; saldo devido pela Avalia zero (o
  split paga a revenda direto); estoque e lotes; Anexo I com os clientes
  ativos e Anexo II com a conciliação, na posição da emissão.
- Estoque - o comprado é da revenda, sem devolução: as placas em estoque têm
  baixa na data efetiva (opção "manterá as placas, mas não poderá ativá-las").
- Assinado o distrato - vendas, pedidos e qualquer escrita param, e a equipe
  perde o acesso. As placas no ar (dentro da carência), os clientes delas e os
  links curtos desses clientes passam para a revenda da Avalia (a do CNPJ da
  Avalia), que renova pelo valor da venda antiga. O QR continua respondendo. A
  venda e o razão ficam com a revenda antiga.
- O revendedor entra só para consultar, exportar os dados (arquivo JSON com
  clientes, vendas, placas e razão) e baixar o distrato, por 30 dias; depois o
  login é desligado. Os registros que a lei exige continuam guardados.
- Revenda que nunca assinou o contrato não tem o que distratar: encerra na
  hora.
- Conta encerrada mostra a faixa "Conta encerrada" em toda tela e esconde o
  que só serve para vender (Nova venda, Serviços, Catálogo, Marca e Mapa). A
  busca no Google também é recusada. Importação de placas é recusada, também
  no ensaio.
- Reabertura - o revendedor pede pela faixa de conta encerrada, com motivo
  opcional. O pedido vira um chamado no suporte e aparece para a operadora em
  Revendedores, que aprova ou recusa. Aprovada, a conta volta, o revendedor
  recebe aviso no sino e por e-mail, assina um contrato novo antes do próximo
  pedido e religa a equipe pela tela Equipe. O que o distrato fez fica: os
  clientes transferidos continuam com a Avalia e o estoque baixado não volta.

### 6.14 Revenda suspensa

- A operadora suspende e reativa a revenda, com motivo. Suspensa, a revenda
  só consulta: não vende nem pede placas. As placas vendidas seguem no ar. O
  revendedor recebe o aviso no sino.

### 6.15 Auditoria

- Fica registrado quem fez, o quê, onde, o valor de antes e o de depois:
  destino da placa (na venda e nas trocas), suspender e pôr no ar, troca de
  placa, correção, cancelamento, cortesia, recebido por fora e o desfazer,
  equipe (cadastro, alteração, desativação, exclusão), comissão padrão,
  produto, repasse, marca, links, cadastro e anonimização do cliente,
  fechamento e reabertura do mês e cadastro, alteração e exclusão de sócio.
- A ficha da placa mostra o histórico dela. A tela Auditoria, só do
  revendedor, mostra tudo.
- Registro não se altera nem se apaga, nem pela aplicação. A anonimização
  registra o fato sem o nome do cliente.
- A operadora tem a auditoria dela: toda mudança que ela faz na plataforma e
  deu certo fica registrada com quem, quando, a ação, o alvo e o que foi
  enviado. Ação nova da operadora entra sozinha, sem código a mais.

## 7. Dinheiro

### 7.1 Cobrança

- No balcão, Pix (QR na tela, válido por 30 minutos) ou cartão de crédito em
  até 12 vezes. A tela mostra a parcela e o total de cada opção antes de
  cobrar. O cartão vai do celular direto ao Pagar.me; a plataforma não vê
  número nem código.
- Tarifa do Pagar.me, nas condições acordadas - Pix 1,09%; cartão pelo custo
  efetivo com recebimento em 2 dias (5,11% à vista, 8,17% em 2x, até 22,09% em
  12x); R$ 0,55 de gateway e R$ 0,44 de antifraude por transação; R$ 3,67 por
  saque.
- A revenda paga a tarifa no split. No Pix e no cartão à vista, a Avalia
  recebe a parte dela já sem a fatia dela na tarifa (landing page no Pix -
  R$ 711,36 em vez de R$ 719,20).
- Parcelado de 2x a 12x - o cliente paga o custo do parcelamento. A landing
  page de R$ 899,00 em 12x sai por R$ 1.153,90, e a revenda e a Avalia recebem
  como à vista.
- Uma venda tem no máximo uma cobrança viva (pendente ou paga) por vez.
- Uma cobrança só anda para a frente - pendente vira paga, cancelada ou
  falha; paga só vira estornada. Aviso atrasado não desfaz pagamento.
- Cobrança que não chegou ao Pagar.me é reencontrada pelo código. Sem pedido
  depois de 15 minutos, vira falha e pode ser refeita.
- Estorno e chargeback feitos no painel do Pagar.me também chegam aqui, pelo
  aviso do Pagar.me ou pela reconsulta diária da cobrança paga.

### 7.2 Comissão e repasse

| Ponto | Regra |
| --- | --- |
| Quem ajusta | Só o revendedor; toda mudança vai à auditoria |
| Níveis | Padrão da revenda, exceção por produto, exceção por vendedor; vale a mais específica |
| Base | O que fica com a revenda - na placa, o valor bruto da venda; no serviço, o valor menos a parte da Avalia. Exemplo - landing page a R$ 899,00 com 20%: R$ 35,96 (20% de R$ 179,80) |
| Forma | Percentual em pontos base (2000 = 20%) |
| Limite | Até 50% da base; a parte da Avalia nunca entra na conta da comissão |
| Mudança | Vale para venda nova; venda feita mantém o percentual gravado |
| Cortesia e renovação | Sem comissão |
| Estorno | Lançamento negativo no saldo do vendedor, descontado do próximo repasse |
| Repasse | O revendedor faz o Pix ao vendedor e registra na tela, com data de corte opcional e comprovante. O repasse paga a comissão devida até o corte, menos o já repassado. Sem saldo, recusa |
| Repasse desfeito | Só o último de cada vendedor; a comissão volta ao saldo |
| Saldo | O vendedor vê a comissão a receber e a já recebida no início e em Minhas vendas |

Exemplo - venda de placa de R$ 100,00 com comissão de 20%: R$ 100,00 para a
revenda (de onde sai a tarifa do Pagar.me) e R$ 20,00 lançados como devidos ao
vendedor.

### 7.3 Razão

- Só inserção. Correção é uma linha nova com o valor contrário, que aponta
  para a corrigida, na competência do dia do estorno.
- Venda paga lança a receita bruta, a taxa da Avalia e a comissão devida.
  Renovação paga lança receita e taxa, sem comissão. Repasse lança a comissão
  paga. O mesmo pagamento nunca é lançado duas vezes.
- Na venda em grupo, o razão lança cada venda pelo valor dela; a última do
  grupo fica com o custo do parcelamento e os centavos do rateio da tarifa.
- Competência no fuso de Brasília. O líquido do mês é receita menos taxa
  menos comissão; a tarifa do Pagar.me sai no extrato do Pagar.me.
- Também vão ao razão a despesa, a retirada de sócio, a perda de placa e o
  custo da placa dada em cortesia (seção 7.4).
- Mês fechado não muda e não recebe linha: a que chega para ele entra no mês
  corrente.

### 7.4 Financeiro da revenda

- Despesa - data (o mês da data é a competência), descrição, categoria
  (placas e material, frete, marketing, software e ferramentas, impostos,
  taxas, serviço de PJ com nota, outros) e valor. Vai ao razão como linha;
  editar estorna a linha em vigor e lança a nova, excluir só estorna. Data no
  futuro é recusada.
- Serviço de PJ com nota - o pagamento a quem divide o resultado sem estar no
  contrato social e emite nota entra no resultado como despesa, e não como
  retirada.
- Sócio do contrato - nome, como no contrato social, e CPF opcional. Só ele
  recebe retirada. O parceiro comercial que não está no contrato é vendedor
  com comissão, na Equipe. Sócio com retirada não se exclui.
- Retirada de sócio - sócio, data, natureza (pró-labore, distribuição de
  lucros, adiantamento), valor e observação. Vai ao razão como a despesa, mas
  fica abaixo do resultado: não é despesa.
- Custo da placa - o que a revenda pagou por ela no pedido de placas, com o
  cupom, dividido pela quantidade, congelado na ativação do lote e copiado na
  venda. O custo da placa vendida acompanha a receita da venda (mesmo mês,
  mesmo estorno). A placa dada em cortesia lança o custo no mês da cortesia; a
  cortesia cancelada estorna.
- Perda - placa perdida ou quebrada (baixa em estoque, troca com esse motivo,
  venda cancelada em que a placa voltou com defeito ou não voltou), pelo custo
  dela. Baixa desfeita estorna a perda. Não são perda - defeito de impressão
  (o acrílico volta com outro QR), lote arquivado, código não impresso e
  encerramento da conta.
- DRE do mês - vendido (vendas registradas no mês que seguem de pé), recebido
  (pagamentos menos estornos), receita reconhecida, menos taxa da plataforma,
  comissões, custo das placas, perdas e despesas por categoria, igual a
  resultado. Retiradas de sócio, por natureza, ficam abaixo do resultado.
- Receita reconhecida - a placa vendida ou renovada é uma obrigação de 12
  meses (o QR passa pela plataforma). A receita entra em 12 partes a partir do
  mês do pagamento, e o custo da placa vendida acompanha. Serviço entra
  inteiro no mês. Comissão e taxa entram no mês do pagamento. O que já entrou
  no caixa e ainda não no resultado aparece como "a reconhecer". Estorno para
  a apropriação e devolve no mês dele o que já tinha entrado.
- Fechamento do mês - o revendedor fecha mês que já acabou. Fechado, a
  despesa ou retirada com data nele é recusada, e a correção de linha dele vai
  ao mês corrente. O pagamento do mês fechado que chega depois também entra no
  mês corrente. Reabrir pede o motivo. Fechar e reabrir vão à auditoria.

## 8. Dados pessoais (LGPD)

- Papéis - a revenda é a controladora dos dados dos clientes e da equipe
  dela; a Avalia, como plataforma, é a operadora. A Avalia é controladora só
  dos dados das revendas (cadastro, contrato, pedidos).
- Dado mínimo - do cliente, empresa, responsável e telefone; CNPJ ou CPF e
  e-mail só quando há cobrança, porque o Pagar.me pede. Do lugar no Google, só
  o id.
- Direitos do titular - na ficha do cliente, o revendedor exporta tudo o que a
  plataforma guarda sobre ele (dados, vendas, placas, cobranças) em JSON, e
  anonimiza. A anonimização apaga nome, responsável, telefone, documento,
  e-mail e lugar, não volta atrás, e é recusada com cobrança em aberto. Vendas
  e razão ficam, com os valores, pela obrigação fiscal.
- Logs e Sentry sem dado pessoal: nem corpo, nem cookie, nem IP.
- Fim do contrato - pelo distrato, a revenda exporta os dados por 30 dias.
  Depois, a Avalia elimina ou anonimiza o que não precisar manter e guarda o
  que a lei exige (cláusulas 3.3 do distrato e 8.9 do contrato).
- O texto da política de privacidade espera a revisão jurídica (seção 13).

## 9. Marca da revenda

O revendedor configura, na tela Marca, o nome exibido (até 40 caracteres), a
cor primária, a cor de destaque, o logo e se o cabeçalho é branco ou na cor
primária (com o logo num fundo branco). Vale para todos os usuários da revenda
no app. A tela mostra a prévia e o contraste antes de salvar, e a API confere
de novo:

- Cor primária - fundo de botão. O texto por cima (branco ou escuro, o de
  maior contraste) precisa de 4,5:1.
- Cor de destaque - anel de foco e realce do menu. Precisa de 3:1 sobre o
  branco.
- Logo - qualquer foto ou imagem de até 25 MB. O navegador reduz para 512 px
  no lado maior e grava em WebP (PNG onde não há WebP). A API guarda até
  512 KB, conferido pelos bytes do arquivo.

A tela de entrada e a área da operadora usam a marca da plataforma. Voltar à
marca da plataforma apaga cores, nome e logo. O e-mail enviado em nome da
revenda leva o nome e a cor dela (seção 6.11).

## 10. Fora do escopo

- Avalia Gestor, Lia, página de links e loja, que seguem no sistema anterior
  da Avalia.
- WhatsApp como integração e atendimento por IA. O atendimento é feito nos
  chamados, dentro da plataforma. O WhatsApp aparece só como link com a
  mensagem pronta (convite, Pix em aberto, renovação).
- Domínio próprio por revenda. Todas usam o mesmo domínio.

## 11. A Avalia na plataforma

A Avalia vendia placas num módulo do sistema anterior (o Sales). Desde
24/10/2026, a revenda Avalia vende pela plataforma, como qualquer revenda.

- Importação - as placas do Sales entraram na revenda Avalia pela importação
  que serve a qualquer revenda (49 ativas, 2 suspensas e 248 em branco no
  arquivo de importação), nos lotes de origem, com os e-mails e os lugares no
  Google dos clientes completados. A venda importada mantém o valor de origem,
  sem cobrança, e a comissão devida vira saldo do vendedor.
- Endereço impresso - o sistema anterior repassa
  `avaliaone.com.br/q/{codigo}` para `sales.avaliaone.com.br/q/{codigo}`, com
  302 e sem cache. Venda, troca, renovação, destino e suspensão de placa são
  feitos só na plataforma.
- Sem volta - o que quebrar se corrige na plataforma, ou com a placa
  regravada (cliente sazonal). Os totais foram conferidos depois da virada.
- Repasse - o do `/q/` pelo sistema anterior continua para sempre, porque as
  placas impressas apontam para avaliaone.com.br/q/. O próximo passo é
  desligar o módulo de vendas do sistema anterior.
- Recebedor - a revenda Avalia recebe no mesmo recebedor da conta da Avalia
  no Pagar.me: a cobrança vai numa regra só do split e há um saque só. O
  pedido de placas dela tem o pagamento registrado pela operadora, sem passar
  pelo Pagar.me.
- Ferramentas (docs/OPERACAO.md) - reserva dos códigos de outro sistema,
  conversor do backup diário do Sales no arquivo de importação, importação de
  placas e de lotes de origem, com ensaio que só mostra os totais, e
  complemento dos clientes.

Regra que não quebra - `avaliaone.com.br/q/{codigo}` responde para sempre,
porque está impresso nas placas vendidas.

### O que veio do Sales

| No Sales | Na plataforma |
| --- | --- |
| Código Crockford de 6 caracteres e `/q/{codigo}` | Igual, com `/q/` e `/Q/` |
| Lote LT-0001, ZIP com SVG, PNG e CSV para o Corel | Igual, gerado pela operadora |
| Compra de placas da gráfica com custo congelado | Pacote pago pela revenda; custo da placa igual ao preço pago dividido pelas placas |
| Consignação ao vendedor, 20 por vez, tudo ou nada | Igual, só dentro da revenda |
| Situação e estado, 12 meses, carência de 30 dias | Igual |
| Contagem de leitura por dia, sem derrubar o redirecionamento | Igual, com soma atômica no Postgres |
| Valor nunca abaixo da tabela, até 5 vezes | Igual |
| Comissão de 25% sobre a venda, sócio sem comissão | Percentual por revenda, produto e vendedor; revendedor sem comissão. Na importação, os dois sócios entraram como vendedores com 50% cada |
| Venda só na Nova venda, placa sem venda fora do ar | Igual |
| Pix e cartão pelo Asaas | Pix e cartão pelo Pagar.me, com split |
| Busca do lugar no Google Places e link colado do perfil | Places (New) Text Search pelo servidor e link colado |
| Mapa de bairros em Leaflet com OpenStreetMap | Google Maps JavaScript, por cidade |
| Placa coringa de teste | Lote da categoria teste |
| Renovação por 60% do valor, sem comissão | Igual, com o percentual por revenda definido pela operadora |
| Confirmação de pagamento em dinheiro ou outro Pix | Igual, sem Pix em aberto; o que seria da Avalia fica a acertar no razão; o revendedor desfaz com estorno |
| Cortesia, cancelamento e troca de placa com estorno no razão | Igual, com motivo; a venda paga sai por estorno no Pagar.me, que já desfaz o razão |
| Pix e confirmação pelo WhatsApp | E-mail pelo Resend; WhatsApp só por link com a mensagem pronta |

## 12. Fases e integrações

| Fase | Conteúdo | Situação |
| --- | --- | --- |
| 0. Base | Solução em camadas, login, isolamento entre revendas, CI e publicação | Em produção |
| 1. Revenda | Cadastro, contrato, pedido, lote, equipe, venda, NFC e QR | Em produção |
| 2. Pagamento | Cobrança da venda por Pix e cartão com split, checkout do pedido de placas, cupom, comissão, razão | Em produção desde 23/10/2026 |
| 3. Serviços | Chamados, avisos ao vivo, mapa, Worker, conexão com o Protheus, marca da revenda, serviços vendidos | Em produção; o pedido de venda ao Protheus espera na fila até o serviço do Protheus ser configurado |
| 4. Avalia | Placas do Sales na plataforma e repasse do endereço antigo | Em produção desde 24/10/2026, com os totais conferidos; falta desligar o módulo de vendas do sistema anterior |
| 5. Encerramento | Distrato gerado pela plataforma, transferência dos clientes ativos, exportação | Em produção |

| Integração | Situação | Variáveis |
| --- | --- | --- |
| Pagar.me | Em produção desde 23/10/2026, na conta da Avalia (marketplace), com recebedor `re_` e webhook ligado; uma venda real paga e estornada. Falta um estorno parcial real e o ensaio do cartão em produção | `Pagarme__ChaveSecreta`, `Pagarme__ChavePublica`, `Pagarme__RecebedorAvalia`, `Pagarme__SegredoDoWebhook` |
| Recebedor da revenda | A operadora informa o recebedor (`re_` ou `rp_`) em Revendedores, depois de criá-lo no painel do Pagar.me. O cadastro pela API (`POST /recipients`, com KYC) ainda não existe | - |
| D4Sign | Em produção no ambiente de testes, sem validade jurídica; falta passar para a conta de produção (`secure.d4sign.com.br`) | `D4Sign__UrlBase`, `D4Sign__TokenApi`, `D4Sign__CryptKey`, `D4Sign__SegredoHmac`, `D4Sign__Cofre` |
| Resend | Em produção, com o domínio sales.avaliaone.com.br verificado | `Resend__ChaveApi`, `Resend__Remetente`, `Resend__ResponderPara` |
| Google Places | Em produção, com a chave do servidor restrita ao IP do VPS | `Google__ChaveDoServidor` |
| Google Maps no navegador | Em produção, com a chave do navegador; o Map ID próprio está a conferir (sem ele, vale `DEMO_MAP_ID`) | `Google__ChaveDoMapa`, `Google__IdDoMapa` |
| BrasilAPI | Em produção, sem chave | - |
| Gotenberg | Em produção, em contêiner sem rede | `Pdf__UrlDoConversor` |
| Protheus | Conexão REST pronta. Sem endereço configurado, o pedido de venda espera na fila do Worker | `Protheus__UrlBase`, `Protheus__Usuario`, `Protheus__Senha` |
| Sentry | Em produção, com monitor de disponibilidade e check-in do Worker | `Sentry__Dsn` |
| SonarCloud | Projeto `Hitice_Avalia-Sales` analisado pelo CI desde 25/10/2026 (C#, TypeScript e cobertura da API); falta desligar a análise automática no painel. O token vence em 24/11/2026 | `SONAR_TOKEN`, `SONAR_ORGANIZACAO`, `SONAR_PROJETO` |

## 13. Decisões pendentes

| Decisão | Opções | Situação |
| --- | --- | --- |
| Recebedor da revenda Avalia | Mesmo recebedor da conta do marketplace ou um separado | Hoje é o mesmo, com a cobrança numa regra só. Separado, o extrato da revenda não se mistura com a parte da Avalia nos serviços |
| Comissão na renovação | Renovação sempre sem comissão ou comissão de renovação ligada pela revenda | Hoje sem comissão; a opção de ligar não existe |
| Texto do termo do cliente final | Rascunho ou revisão jurídica | Espera a revisão jurídica; a venda ainda não gera o termo |
| Política de privacidade | Texto | Espera a revisão jurídica |

## 14. Registro de mudanças

| Data | Mudança |
| --- | --- |
| 25/10/2026 | Tela de entrada com os logos oficiais do Pagar.me e da Stone, nas cores das marcas; o Resend sai. |
| 25/10/2026 | Mapa abre mais perto - com oito lugares ou mais, o enquadramento fica com os 80% do meio, e o lugar isolado na ponta da cidade aparece ao arrastar; zoom até o nível da rua. |
| 25/10/2026 | Documento revisto - texto reorganizado, estado das integrações e das fases atualizado. |
| 24/10/2026 | Versão 2.7.0 - o Catálogo sai da aba de Vendas e vira item do menu do revendedor e do vendedor (/catalogo); toda tabela rola dentro dela com o cabeçalho fixo e, a partir de sete linhas, ganha a busca pelo texto da linha (sem acento); as listas de links e NFC também; confirmação na tela (toast) ao gravar; olho para mostrar a senha; conteúdo até 1080 px no computador; Financeiro cabe numa tela; ações da linha sempre no menu de três pontos e selo de situação com largura fixa no estoque, com filtro por vendedor e os lotes em tabela; Comissões com o repasse sempre à mão (desligado, com o motivo, quando não há o que pagar); Razão com a coluna Condição para o estorno; a renovação (% da venda) passa a ser definida pela operadora em Revendedores, e a revenda só lê; Clientes com cadastro antes da venda e Exportar e Anonimizar dentro da ficha; Equipe com CPF do vendedor; Auditoria busca por placa, cliente, vendedor ou ação; Segurança convida a ligar o segundo fator; Marca com o cabeçalho branco ou na cor primária (logo num fundo branco), e o Salvar ao lado do Restaurar; Mapa no tema escuro do Google; início do vendedor com as vendas dos últimos 30 dias e a rosca da comissão; leituras da placa nos últimos 30 dias com eixo; Meus dados explica que o CNPJ é o do contrato; recuperação de senha com o botão Enviar por e-mail. Para o vendedor - meta do mês definida pelo revendedor na Equipe, com a barra de progresso no início; ranking da equipe no mês por vendas pagas, sem valor nem comissão dos outros; Carteira (/carteira) com as placas vendidas, vencimento, leituras dos últimos 30 dias e os botões Renovar e WhatsApp com a mensagem pronta (o revendedor vê a carteira da revenda inteira); Pix em aberto no detalhe da venda, para copiar ou mandar pelo WhatsApp ao cliente que não está no balcão. |
| 24/10/2026 | Versão 2.6.0 - detalhe da venda, despesa de serviço de PJ com nota, a /now de volta ao quadro de partidas, e o primeiro lote da revisão de arquitetura (docs/SDD.md). |
| 24/10/2026 | Detalhe da venda (/painel/vendas/{id} e /vendedor/vendas/{id}, GET /vendas/{id}) - aberto pelo nome do cliente na lista: produto, placa, valor, vendedor, comissão (e se já foi repassada), vencimento, cancelamento com motivo; o cliente; o pagamento (situação, meio, valor da cobrança, estornado, datas); os itens da mesma cobrança, cada um com o detalhe dele; as ordens de serviço; e as linhas do razão da venda, só para o revendedor. Gerar cobrança, receber por fora, trocar a placa, corrigir valor e vendedor, cortesia e cancelamento são as mesmas ações da lista. O vendedor só abre a venda dele. |
| 24/10/2026 | Despesa na categoria Serviço de PJ com nota - o pagamento a quem divide o resultado sem estar no contrato social e emite nota (o caso do sócio de fato PJ) entra no resultado como despesa, não como retirada. |
| 24/10/2026 | Design system no padrão da categoria (DESIGN.md) - base neutra em OKLCH com a cor da revenda só na ação principal, no item ativo, na seleção e no dado principal do gráfico; selos de situação em verde, âmbar, vermelho e neutro; Início no esboço aprovado (indicadores, 30 dias, para onde foi o dinheiro e vendas de hoje); movimento de 150 a 200 ms na etapa da Nova venda e na confirmação do Pix; o lint recusa degradê, vidro, roxo, valor solto e cor fora dos tokens. |
| 24/10/2026 | Página pública /now (sales.avaliaone.com.br/now), estática em web/public/now - apresentação técnica, na ordem: abertura (Avalia Sales, reconstruído a partir do sistema anterior em Laravel/PHP, com a produção lida ao vivo do /api/health e as telas); arquitetura e stack por camada, como está no código, e a comparação entre o sistema anterior e a implementação atual; quatro decisões de engenharia (isolamento por revenda, pagamento confirmado no gateway, razão append-only, migração incremental), cada uma com contexto, decisão, motivo e evidência no repositório; a venda em seis etapas, ligada às decisões; qualidade e operação, com a contagem de testes dita como contagem; aprofundamento com PDD, SDD, ADRs, operação, contrato OpenAPI e código. O detalhe fica em blocos que abrem. Fundo branco, Geist, magenta do logo. |
| 24/10/2026 | O papel do dono da revenda aparece sempre como Revendedor: no menu lateral, nas mensagens da API, nos dados de demonstração e nos documentos. O valor interno do papel continua Dono. |
| 24/10/2026 | README reescrito: como a plataforma funciona, a venda passo a passo, arquitetura, integrações, stack e as telas. |
| 24/10/2026 | Vendas é a casa da venda - o Catálogo vira a aba Catálogo dela (o endereço /catalogo leva para lá) e sai da barra de baixo do celular e do menu lateral. A Nova venda passa a ser escolha de itens com a venda montada na tela: a placa é um item que abre a leitura do QR e o destino; o serviço entra com o pedido mínimo e a quantidade ajustável, nunca abaixo dele; o valor de cada linha pode ser ajustado; tudo sai numa cobrança só (POST /vendas/itens), e o teste gratuito só aparece quando a venda tem só placas. A tela da venda registrada mostra cada placa com o NFC e o link, e o Pix do total. A lista de vendas mostra a quantidade ("10x Kit Mesa") e marca as vendas feitas juntas; a operadora define o pedido mínimo do serviço no Catálogo da Avalia; a ordem de serviço mostra a quantidade. |
| 24/10/2026 | Venda com vários itens para o mesmo cliente (até 20): cada placa ou serviço é uma venda, com a comissão dela, e juntas formam um grupo com uma cobrança só (POST /vendas/itens). Pago, o razão lança cada venda pelo valor dela; a última do grupo fica com o custo do parcelamento e os centavos do rateio da taxa. Cancelar uma venda do grupo pago devolve só a parte dela (estorno parcial no Pagar.me, ou por fora quando foi recebido por fora); com o Pix do grupo em aberto, o Pix cai inteiro e o resto ganha cobrança nova em Vendas. Serviço vende por quantidade, nunca abaixo do pedido mínimo do catálogo: kit mesa e placa de cardápio a partir de 10; a ordem de serviço leva a quantidade. |
| 24/10/2026 | Área Financeiro do dono (menu Equipe e dinheiro) - DRE do mês, gráficos, despesas, sócios e retiradas, fechamento do mês. |
| 24/10/2026 | Despesa com data, descrição, categoria e valor vai ao razão; editar estorna e lança de novo, excluir estorna. |
| 24/10/2026 | Só o sócio cadastrado como do contrato social recebe retirada; o parceiro comercial fora do contrato segue como vendedor com comissão. |
| 24/10/2026 | Retirada de sócio com natureza (pró-labore, distribuição de lucros, adiantamento) vai ao razão e fica abaixo do resultado, fora das despesas. |
| 24/10/2026 | Custo da placa é o valor pago no pedido de placas, com o cupom, dividido pela quantidade, congelado na ativação do lote; o da placa vendida acompanha a receita da venda. |
| 24/10/2026 | Perda pelo custo da placa: perdida ou quebrada em estoque, na troca ou na venda cancelada que não voltou ou voltou com defeito. Defeito de impressão não é perda. Cortesia lança o custo da placa no mês. |
| 24/10/2026 | Receita da placa (venda e renovação) reconhecida em 12 partes a partir do mês do pagamento, com o custo da placa junto; serviço inteiro no mês; comissão e taxa no mês do pagamento. |
| 24/10/2026 | DRE do mês com vendido, recebido, receita reconhecida, taxa, comissões, custo das placas, perdas, despesas por categoria e resultado; retiradas abaixo do resultado. |
| 24/10/2026 | Fechamento do mês pelo dono, só de mês que já acabou: mês fechado não recebe linha, a correção vai ao mês corrente, e reabrir pede motivo e vai à auditoria. |
| 24/10/2026 | Cancelamento completo: a venda se cancela antes do estorno (motivo ou observação inválidos recusam antes de o dinheiro voltar). O estorno, venha do cancelamento ou do painel do Pagar.me, estorna o razão, cancela a ordem de serviço aberta, avisa a operadora da ordem já entregue para tirar do ar o que foi entregue, e encerra (marca como lido) o que a venda abriu no sino de dono, vendedor e operadora. Estorno feito só no painel avisa o dono para cancelar a venda. O vendedor recebe o aviso da venda dele cancelada, com a comissão estornada. |
| 24/10/2026 | Painel lateral do computador com a cara da plataforma - logo no topo, a revenda e o papel de quem entrou, Nova venda em destaque, menu em grupos com o item ativo marcado e a conta (Meus dados, Segurança, Sair) no pé. Celular e tablet sem mudança. |
| 24/10/2026 | Estorno recusado pelo Pagar.me (por exemplo, sem saldo disponível na conta para devolver o Pix) aparece como recusa clara no cancelamento, e nada muda: a venda segue paga. Sem saldo, o estorno sai pelo painel do Pagar.me, que devolve a taxa junto, e depois o cancelamento aqui fecha a venda sem pedir outro estorno. |
| 24/10/2026 | A tela da venda registrada espera o pagamento também na venda de serviço (antes esperava sem fim) e para quando o Pix vence, com o aviso para gerar outro em Vendas. No computador, o botão da Nova venda fica logo abaixo do cartão, na coluna da venda, e não preso no rodapé da janela. |
| 24/10/2026 | Ficha do cliente para o dono: editar (com o lugar no Google, que põe no mapa), ver placas e vendas, juntar cadastro duplicado (vendas, links e placas passam ao que fica) e excluir o que não tem venda. Venda corrigida pelo dono antes do repasse: valor só sem cobrança viva nem paga; vendedor também na venda paga, com a comissão estornada de um e devida ao outro. Operadora suspende e reativa a revenda: suspensa não vende nem pede placas, e as placas vendidas seguem no ar. Repasse desfeito (só o último de cada vendedor): a comissão volta ao saldo. Ordem de serviço entregue ou cancelada por engano se reabre; a cancelada por estorno, não. A coluna Placas da lista de clientes conta só placas em venda ativa. |
| 24/10/2026 | Cancelamento com motivo (erro de cadastro, não pagou, desistência, defeito, outro), observação e o destino da placa (volta ao estoque, voltou com defeito, não voltou). A cobrança viva não trava mais: o Pix em aberto é cancelado no Pagar.me, o pago volta inteiro ao cliente pelo Pagar.me e o recebido por fora é desfeito; o estorno vai ao razão na hora, inclusive da comissão, que desconta no próximo repasse se já foi repassada. Teste gratuito de 7 dias, escolhido na cobrança da Nova venda: sem cobrança na hora; dois dias antes do fim o dono e o vendedor são avisados; no fim, sem pagamento, a placa sai do ar e a venda mostra "teste vencido: receber ou devolver". Pagou, a placa volta e os 12 meses contam do pagamento. |
| 24/10/2026 | Troca de placa com motivo (defeito de impressão, quebrada, perdida): a placa antiga fica encerrada com o motivo e guarda a venda de onde saiu, e a baixa dela não se desfaz. O vendedor troca na venda dele, com placa que está com ele; o dono, com qualquer placa livre. Reimpressão de placa com defeito: um lote novo, com o título dizendo de que lote é. |
| 24/10/2026 | Lote com título e categoria (placa, crachá, cardápio, teste, avulso). Lote só se exclui antes do primeiro download e sem despacho; os códigos dele ficam reservados e nunca voltam ao sorteio. Depois, só se arquiva: a operadora arquiva o lote livre, e o dono, o lote que já está no estoque dele; as placas em estoque saem com o motivo "lote arquivado" e desarquivar devolve. Baixa de placa sem venda com motivo (defeito de impressão, quebrada, perdida, não impressa), que o dono desfaz. As placas do Sales voltam aos lotes de origem, com o título de lá, e os de teste saem arquivados. |
| 24/10/2026 | Telefone sem o 55 do país e sem o 0 da operadora: "+55 34 9…" e "34 9…" são o mesmo cliente. Renovação de cortesia e de venda de R$ 0 sai pelo percentual sobre a tabela da venda, e não por R$ 0,01. Arte do QR igual à da gráfica do Sales (caminho único, 30 mm, fundo transparente), com o miolo vazado e a marca pequena, branca e chapada; download em SVG e PNG, só SVG ou só PNG. Mapa com uma aba por cidade, aberto na que tem mais lugares. |
| 23/10/2026 | Estorno de venda de serviço (pelo Pagar.me ou recebimento por fora desfeito) cancela a ordem de serviço ainda não entregue e avisa a operadora no sino; a entregue fica como está. |
| 23/10/2026 | Recebedor do Pagar.me aceito com `rp_` ou `re_`, na revenda e no recebedor da Avalia: a conta da Avalia gera `re_`. A revenda Avalia usa o mesmo recebedor da operadora, e o pedido de placas dela tem o pagamento registrado pela operadora, sem passar pelo Pagar.me. |
| 23/10/2026 | Parcelado de 2x a 12x com o custo do cartão por conta do cliente, com a parcela e o total na Nova venda. No Pix e à vista, a Avalia recebe a parte dela sem a fatia dela na tarifa. Perfil no Google com placa passa a 20% de desconto da revenda, como os outros serviços. |
| 23/10/2026 | Comissão do serviço sobre o que fica com a revenda (valor menos a parte da Avalia), para a comissão não comer a margem. Na placa, segue sobre o valor bruto. |
| 23/10/2026 | Parte da Avalia no serviço - a revenda tem 20% de desconto sobre o sugerido em todos os serviços e 15% no Perfil no Google com placa; o resto vai para a Avalia pelo split. O custo que a revenda vê no catálogo já inclui a parte da Avalia; recebido por fora, a parte fica a acertar no razão. |
| 23/10/2026 | Taxa da Avalia zerada - a venda e a renovação vão inteiras para a revenda pelo split, que paga a tarifa do Pagar.me; a Avalia ganha nas placas e nos serviços. Venda já feita guarda a taxa dela. O razão e as Configurações só mostram a taxa quando ela existe. |
| 23/10/2026 | Pedido de placas pode sair para retirada na Avalia - a operadora escolhe "Pronto para retirada" em vez de Despachar, o dono recebe o aviso de que a caixa está pronta e ativa o lote igual. Revenda com o mesmo recebedor da Avalia é cobrada numa regra só do split. Logos das integrações no pé da tela de entrada. |
| 23/10/2026 | Tabelas cabem no cartão - com mais de uma ação, a linha mostra a principal (Editar) e as outras no botão "⋯" (Equipe, Pacotes, Cupons); o formulário lateral (Novo vendedor, Novo cupom e outros) fica ao lado só a partir de 1280 px; tabela mais larga que a tela mostra sombra na borda que rola. |
| 23/10/2026 | A plataforma tem uma operadora só, que cuida da operação inteira; a Avalia vende como revenda comum. Sai o cadastro temporário de operadora da tela de entrada. |
| 23/10/2026 | Troca de senha logado, em Meus dados, para todos os papéis - pede a senha atual, derruba os outros aparelhos e avisa por e-mail. Vale também com a conta encerrada. |
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
