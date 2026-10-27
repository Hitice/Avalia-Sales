# Avalia Sales - documento de produto (PDD)

O que a plataforma faz, para quem, e as regras de negócio. A construção fica
no [SDD](SDD.md). Toda mudança de regra atualiza este documento no mesmo
commit, com uma linha no registro de mudanças do fim.

Situação em 27/10/2026 - em produção em sales.avaliaone.com.br, versão 2.7.0.
A cobrança pelo Pagar.me roda em produção desde 23/10/2026, e as placas da
Avalia leem pela plataforma desde 24/10/2026. O contrato pela D4Sign ainda
usa o ambiente de testes, e a conexão com o Protheus fica desligada até o
serviço do Protheus ter endereço (seção 12).

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

A plataforma tem uma operadora só, a Avalia. A revenda Avalia vende como
qualquer outra, com o mesmo peso: mesmo fluxo, mesmos preços, usuários separados
dos da operadora e CNPJ próprio (39.914.870/0001-01), distinto do da operadora.
Nenhuma regra pergunta se a revenda é a Avalia. Para o resultado
da empresa, a operadora marca no cadastro as revendas que são do mesmo grupo
dela (seção 7.5); a marca é decisão dela e não sai do CNPJ nem do nome.

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
  R$ 1.390,00). O preço do pacote
  cobre a produção e a margem da Avalia.
- Parte da Avalia no serviço - a revenda compra o serviço com desconto sobre o
  preço sugerido (20% em todos), e a Avalia recebe o resto do sugerido pelo
  split, gravado na venda. Exemplo - landing page sugerida a R$ 899,00:
  R$ 719,20 para a Avalia; a revenda que vende a R$ 899,00 fica com
  R$ 179,80. A operadora muda o desconto por produto no Catálogo da Avalia. A
  placa avulsa não tem parte: a revenda já pagou no pedido de placas.
- Sem taxa sobre a venda - a Avalia ganha na venda dos produtos: no pedido de
  placas e na parte dela nos serviços. A venda e a renovação de placa não
  mandam nada à Avalia, e a revenda paga a tarifa do Pagar.me. A taxa
  percentual por revenda saiu em 27/10/2026 (contrato, cláusulas 1.6 e 5.4).
- Receita da operadora - a parte da Avalia nos serviços entra no pagamento; o pedido de
  placas entra na ativação do lote (seção 7.5).
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
   produção e despacha um lote da mesma quantidade, pelo envio (com a
   transportadora e o código de rastreio) ou para retirada na Avalia. O
   revendedor recebe o e-mail e a notificação com o número do lote.
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

Todas no mesmo app, liberadas pelo papel do usuário. No computador, toda
tabela rola dentro dela, com o cabeçalho fixo; a ação de todo dia da linha
(Editar, Pagar) fica em botão à vista e o resto no menu "⋯" da última coluna,
presa à direita. No celular, cada linha é um cartão que corre com a página, e
a primeira ação vira botão. A partir de sete linhas, a lista ganha
uma busca pelo texto da linha, sem acento. Gravar confirma na tela. A busca
que a API faz nas listas paginadas também acha sem acento e sem diferença de
maiúscula ("otica" acha "Ótica").

### Acesso e conta

| Tela | Quem usa | O que faz |
| --- | --- | --- |
| Entrada, cadastro e senha | Todos | Entrar, cadastrar a revenda (o CNPJ preenche razão social e endereço), confirmar o e-mail, criar e recuperar a senha. Senha com o botão de mostrar. No pé da entrada, o logo do Pagar.me |
| Meus dados | Todos | No menu da conta. Só dados pessoais: nome de todos; nome da empresa, celular e endereço da revenda (o CNPJ é o do contrato; só a operadora o corrige); telefone e chave Pix do vendedor. O e-mail muda pelo Suporte |
| Segurança | Todos | Troca de senha, para todos os papéis, e o segundo fator por app autenticador ou por código no e-mail, com códigos de recuperação, para o revendedor e a operadora. A tela convida a ligar o fator |
| Notificações | Todos | O sino do cabeçalho. Pendências da revenda (contrato, recebimento, celular, endereço, distrato) e avisos ao vivo; o vendedor vê os dele (venda paga, venda cancelada, comissão repassada, placas com ele) |

### Revenda

| Tela | Quem usa | Pensada para | O que faz |
| --- | --- | --- | --- |
| Início do revendedor | Revendedor | Computador e celular | Quatro números do mês (placas em estoque, placas no ar, vendas e recebido), vendas por dia nos últimos 30 dias, o Financeiro do mês (recebido, comissões, custo das placas, parte da Avalia quando há serviço, despesas e resultado, com Abrir para o Financeiro) e as vendas de hoje com a situação |
| Início do vendedor | Vendedor | Celular | Vendas do mês, comissão a receber, esperando pagamento e recebida, placas com ele; meta do mês com a barra de progresso, ranking da equipe, vendas dos últimos 30 dias e a rosca da comissão |
| Nova venda | Revendedor e vendedor | Celular | Itens (a placa abre a leitura do QR e o destino; o serviço entra com a quantidade, a partir do pedido mínimo), cliente, cobrança de tudo numa vez e gravação do NFC de cada placa |
| Vendas | Revendedor e vendedor | Computador e celular | Lista das vendas, com a quantidade do serviço ("10x Kit Mesa") e as vendas feitas juntas marcadas. Ações - gerar cobrança, receber por fora, trocar a placa, corrigir valor e vendedor, cortesia e cancelamento. O vendedor vê só as dele |
| Detalhe da venda | Revendedor e vendedor | Computador e celular | Aberto pelo nome do cliente na lista. Produto, placa, valor, vendedor, comissão (e se já foi repassada), vencimento, cancelamento com motivo; o cliente (o nome abre a ficha dele em Clientes; aqui ficam só o responsável e o telefone, e o aviso de que falta CNPJ ou CPF e e-mail para cobrar); o pagamento (situação, meio, valor, estornado, datas); os itens da mesma cobrança; as ordens de serviço; o Pix em aberto, para copiar ou mandar pelo WhatsApp; as linhas do razão, só para o revendedor. As ações são as da lista. O vendedor só abre a venda dele |
| Ficha da placa | Revendedor e vendedor | Celular | Destino, vencimento, leituras dos últimos 30 dias, histórico e troca; o cliente (link para a ficha dele), o produto, a data da venda e o botão Ver a venda, que leva ao detalhe com valor, vendedor, comissão e pagamento; a renovação só aparece para o revendedor |
| Carteira | Revendedor e vendedor | Celular e computador | Placas vendidas, com o nome do cliente (link para a ficha dele, só para o revendedor), o vencimento e as leituras dos últimos 30 dias, com os botões Renovar (só o revendedor) e WhatsApp (mensagem pronta). O vendedor vê a dele e chama o cliente; o revendedor vê a da revenda inteira e renova |
| Catálogo | Revendedor e vendedor | Computador e celular | Produtos da Avalia com foto, preço sugerido, pedido mínimo e o preço da revenda. O revendedor ajusta o preço e vai a Pedidos pelo botão Pedir placas; Vender abre a Nova venda com o item. O vendedor não vê o custo |
| Clientes | Revendedor | Computador | Clientes da revenda, cadastro antes da venda e a ficha do cliente - editar (com o lugar no Google, que põe no mapa), placas e vendas, juntar cadastro duplicado, excluir o que não tem venda, exportar e anonimizar (LGPD). A coluna Placas conta só placas em venda ativa |
| Serviços | Revendedor e vendedor | Celular | Abas Link (link curto `/L/` de qualquer endereço colado), NFC (grava o link curto na tag), Maps (busca o negócio no Google e cria o link curto da avaliação) e Status (só o revendedor; as ordens dos serviços vendidos). O revendedor edita, desliga e exclui o link |
| Mapa | Revendedor | Computador | Lugares vendidos no Google Maps, em tema escuro, com uma aba por cidade, aberto na que tem mais lugares |
| Estoque | Revendedor (Minhas placas, para o vendedor) | Computador | Placas por situação, com filtro por vendedor; consignação ao vendedor; ativação, lista e arquivamento de lote; baixa de placa com motivo |
| Pedidos | Revendedor | Computador | Pedidos de placas com a situação (aguardando pagamento, pago, em produção, enviado ou pronto para retirada, entregue) e o link de pagamento |
| Equipe | Revendedor | Computador | Criar, editar, desativar e excluir vendedor, com CPF, comissão própria e meta do mês; convite por e-mail ou WhatsApp |
| Comissões | Revendedor | Computador | Saldo de cada vendedor, a confirmação do dinheiro que o vendedor recebeu por fora, repasse com comprovante (o botão aparece desligado, com o motivo, quando não há o que pagar) e o desfazer do último repasse |
| Financeiro | Revendedor | Celular e computador | Abas Resultado (DRE do mês, gráfico de para onde foi o dinheiro, despesas por categoria, recebido e resultado nos últimos 6 meses), Despesas, Sócios (sócios do contrato e retiradas) e Lançamentos (o razão do mês, com a coluna Condição para o estorno), com criar, editar e excluir; fechar e reabrir o mês |
| Configurações | Revendedor | Computador e celular | Aba Geral - comissão padrão, percentual da renovação (só leitura; quem define é a operadora), o contrato de revenda (reenviar, conferir assinatura, baixar cópia) e o encerramento da conta. Aba Marca - logo com recorte (horizontal ou quadrado, arrastar e zoom), cores, nome exibido e cabeçalho branco ou na cor primária, com prévia nas cores escolhidas |
| Auditoria | Revendedor | Computador | Quem mudou o quê, com antes e depois; busca por placa, cliente, vendedor ou ação; filtro pela ação, 20 por página e exportação em JSON |
| Suporte | Revendedor | Computador e celular | Chamados com a Avalia, com o prazo de resposta |

### Operadora

| Tela | O que faz |
| --- | --- |
| Pedidos | Fila de pedidos de placas - produção, despacho do lote pelo envio (com transportadora e código de rastreio) ou para retirada, e estorno do pedido pago ainda não despachado |
| Lotes | Geração e impressão de lotes com título e categoria, exclusão antes do primeiro download e arquivamento |
| Pacotes e Cupons | Cadastro dos pacotes de placas e dos cupons |
| Catálogo da Avalia | Cadastrar e editar produto, foto com recorte, preço sugerido, desconto da revenda (e a parte da Avalia que sai dele), pedido mínimo, ordem e situação |
| Serviços | Fila de ordens de serviço - iniciar, entregar, cancelar e reabrir |
| Revendedores | Revendas, nome e CNPJ, recebedor no Pagar.me, percentual da renovação, contrato em papel, suspensão, encerramento, pedido de reabertura, liberação de cadastros de destino do dia, a marca de revenda do grupo da operadora e a marca de revenda receptora |
| Fornecedores | Cadastro de quem produz e entrega para a operadora (gráfica, acrílico, confecção com dado variável, logística), com criar, editar, desativar e excluir |
| Destinos bloqueados | Domínios que a plataforma recusa como destino de QR e de link curto, e que deixam de redirecionar |
| Atendimento | Fila de chamados pelo prazo, com o atrasado em vermelho |
| ERP | Envios ao Protheus e reenvio; aviso de integração desligada |
| Auditoria da operadora | O que a operadora mudou na plataforma, com o que foi enviado, busca, 20 por página e exportação em JSON |
| Resultado do grupo | Resultado do mês por revenda do grupo, operadora, eliminações e consolidado, a conciliação das eliminações, o recebido e ainda não reconhecido, a receita por revenda, o a acertar das vendas recebidas por fora, as despesas da operadora, o custo de produção por lote e o fechamento do mês (seção 7.5) |

### Público

| Tela | O que faz |
| --- | --- |
| Página do QR (`/q/{codigo}`) | Redireciona ao destino e conta a leitura; placa vencida, suspensa ou baixada mostra uma página que explica |
| Link curto (`/l/{codigo}`) | Redireciona ao destino e conta o clique |

## 6. Regras de negócio

### 6.1 Acesso e conta

- O cadastro público não cria conta que funcione: o dono entra só depois de
  abrir o link enviado ao e-mail e criar a senha, o que confirma o e-mail. O
  link é de uso único e vale 2 dias; o cadastro não confirmado é descartado em
  48 horas e libera o e-mail e o CNPJ. A resposta é a mesma para e-mail e CNPJ
  novos ou já cadastrados, e quem já tem conta recebe o aviso por e-mail. O
  reenvio espera 1 minuto. Captcha (Cloudflare Turnstile, desligado até
  configurar as chaves), limite de cadastros por IP, por e-mail e por CNPJ e
  teto diário de e-mails do cadastro. O dono criado antes desta regra, com o
  e-mail não confirmado, segue no fluxo anterior: painel só com a confirmação,
  código de 6 dígitos e reenvio depois de 1 minuto; cinco códigos errados
  encerram o envio.
- Contrato e avisos só vão para e-mail confirmado. Conta criada pela
  operadora já nasce confirmada. O vendedor não passa por essa trava.
- Entrada de aparelho novo gera um e-mail ao dono da conta, com navegador,
  sistema, horário e IP, e o link para trocar a senha. O primeiro aparelho
  entra sem aviso.
- Ligar ou desligar o segundo fator gera um e-mail ao dono da conta, sem código
  nem link de entrada, com o atalho para trocar a senha. O e-mail do código de
  acesso lembra que a Avalia nunca pede o código por telefone ou mensagem.
- Troca de senha logada, para todos os papéis - pede a senha atual, encerra
  os outros aparelhos e avisa por e-mail. Vale também com a conta encerrada.
- Segundo fator opcional para o revendedor e a operadora. Desativar pede um
  código novo enviado ao e-mail cadastrado; o código do app e os de
  recuperação também valem.
- A conta da operadora precisa do segundo fator ligado para trocar o nome, o
  CNPJ ou o recebedor de uma revenda, registrar pagamento por fora, estornar
  pedido e registrar acerto. Trocar o recebedor avisa o dono da revenda no
  sino e por e-mail (ADR 0027).
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
- Teste gratuito de 7 dias - escolhido na cobrança da Nova venda, só para uma
  placa de avaliação no Google e uma vez por cliente: quem já teve teste compra
  com cobrança. Exige CNPJ ou CPF e e-mail do cliente, porque a cobrança sai
  deles no fim. Não há cobrança na hora. Dois dias antes do fim, o
  revendedor e o vendedor são avisados. No fim, sem pagamento, a placa sai do
  ar e a venda mostra "teste vencido: receber ou devolver". Pago, a placa
  volta ao ar e os 12 meses contam do pagamento.
- Destino do QR - avaliação no Google (lugar buscado no Places ou link colado
  do Perfil da Empresa; o link colado vence a busca) ou link livre do cliente.
  Só http e https, com domínio, até 1.000 caracteres, e nunca o endereço de
  outra placa ou de outro link da plataforma.
- Limite diário de destinos - cada revenda cadastra até 10 destinos novos por
  dia (Brasília): placa vendida com destino, destino trocado, link curto criado
  ou alterado. Passando disso, o dono usa Solicitar liberação, que abre um
  chamado, e a operadora libera mais para aquele dia (ADR 0026).
- Destinos bloqueados - a operadora bloqueia um domínio, e os subdomínios dele
  também. Ninguém cria destino nele, e a placa ou o link que já apontava para
  ele deixa de redirecionar e mostra "Endereço bloqueado", sem mexer na placa.
  O domínio é a chave do registro: corrigir é desbloquear e bloquear o certo.
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
  seria da Avalia pelo split (a parte dela no serviço) fica "a acertar"; a
  placa e a renovação recebidas por fora não deixam nada a acertar. A
operadora quita o a acertar de cada revenda em Resultado do grupo, na aba A
acertar, registrando o que a revenda pagou por fora; o acerto não passa do
devido e o distrato desconta o que já foi quitado (ADR 0028). Acerto é
dinheiro, como o razão: corrigir é excluir e lançar de novo, com auditoria. O dinheiro que o
vendedor recebeu em mãos só vira comissão em repasse depois de o revendedor
confirmar, em Comissões, que ele chegou. Só o
  revendedor desfaz, e só antes do repasse da comissão. A renovação é anual e
  só do revendedor, o vendedor não renova; ela também se confirma por fora, na
  ficha da placa, depois de uma confirmação na tela, e vai à auditoria com quem
  registrou. Só o revendedor desfaz a renovação
  recebida por fora - estorno no razão, a placa volta ao vencimento de antes e
  a renovação reabre. A paga pelo Pagar.me volta pelo estorno de lá.
- Renovação em aberto com Pix vivo segura o cancelamento e a troca, porque,
  paga depois, estenderia uma placa fora da venda. Renovação sem Pix vivo é
  cancelada junto.

### 6.4 Pedido de placas e cupom

- Pagamento pelo checkout do Pagar.me (Pix, cartão ou boleto, tudo para a
  Avalia). O link vale 3 dias e aceita um pagamento só. Pedir o link de novo
  devolve o mesmo enquanto ele valer. Cancelar o pedido com link em aberto
  cancela o link no Pagar.me junto: ele deixa de abrir o checkout.
- A operadora também pode registrar um pagamento recebido por fora.
- Pagamento confirmado - o dono recebe um e-mail a cada pagamento que entra,
  do pedido de placas e da venda, com o cliente, os itens e o valor.
- Estorno do pedido - a operadora desfaz o pedido pago e ainda não despachado:
  o pedido vira cancelado, o uso do cupom volta e os dois lados são avisados. A
  devolução do dinheiro sai pelo painel do Pagar.me ou por fora. Depois do
  despacho, não se estorna por aqui.
- Lote despachado e não ativado - a revenda é lembrada a cada 7 dias, até 3
  vezes, e aos 30 dias a operadora é avisada, porque a receita do pedido só
  entra na ativação.
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
  operadora reenvia pela tela ERP. Sem o endereço do Protheus configurado a
  integração fica desligada: o pedido pago não entra na fila, e a tela ERP
  avisa.

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
- Aviso do vencimento - 30 e 7 dias antes, o dono e o vendedor que vendeu veem
  o aviso no sino, e o cliente recebe e-mail em nome da revenda (se tem
  e-mail), com o contato dela. Cada vencimento recebe cada aviso uma vez; a
  renovação paga recomeça a série.
- A renovação conta a partir do vencimento antigo, mesmo paga com atraso. Uma
  renovação em aberto por placa; pedir de novo devolve o mesmo Pix. Só o
  revendedor renova (seção 6.3).

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
- O comprador não assina documento: o cliente final paga e usa a placa sem
  termo na plataforma. Só a revenda assina com a Avalia.

### 6.13 Encerramento da conta (distrato)

- O revendedor pede em Configurações, aba Geral (Encerrar conta). A plataforma gera o
  distrato com os dados da revenda e manda à D4Sign, como o contrato. Até
  assinar, ele reenvia o e-mail, baixa a cópia ou desiste (o distrato é
  cancelado na D4Sign).
- Preenchido pela plataforma - data do contrato; data efetiva igual à da
  assinatura do distrato; exportação por 30 dias depois dela; saldo devido à
  Avalia igual à parte da Avalia nos serviços recebidos por fora, menos o que
  a revenda já acertou, com
  vencimento em 10 dias da data efetiva; saldo devido pela Avalia zero (o
  split paga a revenda direto); estoque e lotes; Anexo I com os clientes
  ativos e Anexo II com a conciliação, na posição da emissão.
- Estoque - o comprado é da revenda, sem devolução: as placas em estoque têm
  baixa na data efetiva (opção "manterá as placas, mas não poderá ativá-las").
- Assinado o distrato - vendas, pedidos e qualquer escrita param, e a equipe
  perde o acesso. As placas no ar (dentro da carência), os clientes delas e os
  links curtos desses clientes passam para a revenda receptora, que renova
  pelo valor da venda antiga. O QR continua respondendo. A venda e o razão
  ficam com a revenda antiga.
- Revenda receptora - a operadora marca em Revendedores qual revenda recebe os
  clientes de quem encerra (hoje a revenda Avalia). Só uma por vez: marcar
  outra tira a marca da anterior. Sem receptora, o revendedor não consegue
  pedir o encerramento, e a operadora não tira a marca com encerramento em
  andamento. A receptora não encerra a própria conta, e revenda encerrada ou
  com encerramento pedido não vira receptora.
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
- Venda paga lança a receita bruta, a comissão devida e, na venda de
  serviço, a parte da Avalia. Renovação paga lança só a receita, sem
  comissão. Repasse lança a comissão paga. O mesmo pagamento nunca é lançado
  duas vezes.
- Na venda em grupo, o razão lança cada venda pelo valor dela; a última do
  grupo fica com o custo do parcelamento. A parte da Avalia é dividida só
  entre as vendas de serviço, e a última delas fica com os centavos do rateio.
- Competência no fuso de Brasília. O líquido do mês é receita menos parte da
  Avalia menos comissão; a tarifa do Pagar.me sai no extrato do Pagar.me.
- Mês de antes de 27/10/2026 pode ter linha da taxa da plataforma, que saiu:
  ela soma junto com a parte da Avalia no razão, no DRE e no resultado do
  grupo.
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
- Aporte de sócio - a mesma ficha, com a natureza aporte: o sócio põe dinheiro
  na revenda. Entra no caixa e fica fora do resultado e das retiradas do DRE.
- Caixa - começa no primeiro dia de um mês, com o saldo inicial informado pelo
  revendedor (o capital de giro). Daí em diante soma o recebido das vendas e
  renovações e os aportes, e tira a parte da Avalia, a comissão repassada,
  as despesas, as retiradas e os pedidos de placas pagos. A tarifa do Pagar.me
  fica de fora. O que é de antes do saldo inicial continua no razão e no DRE
  dos meses dele, fora do caixa. Um saldo inicial vale por vez: trocar ou
  excluir estorna o anterior e vai à auditoria. O mês do saldo inicial é o
  corrente, o seguinte ou um já passado, e precisa estar aberto.
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
  (pagamentos menos estornos), receita reconhecida, menos parte da Avalia
  (só no mês com venda de serviço), comissões, custo das placas, perdas e
  despesas por categoria, igual a
  resultado. Retiradas de sócio, por natureza, ficam abaixo do resultado.
- Receita reconhecida - a placa vendida ou renovada é uma obrigação de 12
  meses (o QR passa pela plataforma). A receita entra em 12 partes a partir do
  mês do pagamento, e o custo da placa vendida acompanha. Serviço entra
  inteiro no mês. Comissão e parte da Avalia entram no mês do pagamento. O que já entrou
  no caixa e ainda não no resultado aparece como "a reconhecer". Estorno para
  a apropriação e devolve no mês dele o que já tinha entrado. A venda trazida
  do sistema anterior é reconhecida inteira no mês dela, com o custo, como era
  lá: nada do histórico chega aos meses da plataforma.
- Fechamento do mês - o revendedor fecha mês que já acabou. Fechado, a
  despesa ou retirada com data nele é recusada, e a correção de linha dele vai
  ao mês corrente. O pagamento do mês fechado que chega depois também entra no
  mês corrente. Reabrir pede o motivo. Fechar e reabrir vão à auditoria.

### 7.5 Financeiro da operadora e resultado do grupo

O resultado da Avalia empresa é a revenda Avalia mais a operadora. O desenho
segue a consolidação com eliminação das transações internas (CPC 36 / IFRS 10), a receita
reconhecida quando o controle passa ao cliente (CPC 47 / IFRS 15) e a informação por
segmento (CPC 22 / IFRS 8). O preço do pacote da revenda Avalia é o das outras revendas.

- Livro da operadora - o que a operadora ganha de cada revenda, só de inserção, como o
  razão. Estorno é linha contrária que aponta para a estornada.
  - Parte da Avalia nos serviços - lançada junto com o pagamento e estornada junto com o
    estorno. A parte "a acertar" de venda recebida por fora entra marcada como tal. A linha
    antiga da taxa da plataforma, que saiu em 27/10/2026, soma na parte da Avalia.
  - Pedido de placas - receita da operadora na ativação do lote, quando a revenda passa a
    ter o controle das placas. Pago e ainda não ativado é adiantamento: a tela mostra
    "recebido e ainda não reconhecido". Pedido com cupom de 100% não lança.
- Despesas da operadora - data, descrição, categoria (gráfica e material, execução de
  serviço, frete, software e ferramentas, impostos, outros) e valor. Vão ao livro como a
  despesa da revenda: editar estorna e lança de novo, excluir só estorna, data no futuro
  é recusada.
- Fechamento do mês da operadora - fecha mês que já acabou. Fechado, a despesa com data nele
  é recusada, e a correção entra no mês corrente. Reabrir pede o motivo.
- Fornecedores - cadastro da operadora de quem produz e entrega: gráfica,
  acrílico, confecção (a operadora atua em confecções porque a placa leva dado
  variável, o QR de cada uma) e logística de entrega. Um fornecedor pode ter
  mais de uma função. A despesa da operadora e o custo de lote apontam para
  ele, e o despacho do pedido usa o de logística como transportadora.
  Fornecedor com despesa ou custo apontando para ele não se exclui: desativa.
- A acertar - o que cada revenda deve à Avalia pelas vendas recebidas por fora
  (a parte da Avalia nos serviços, líquida de estorno),
  o que já quitou e o saldo. A quitação é caixa e não passa pelo resultado.
- Custo de produção do lote - a operadora informa, por lote, o que a gráfica cobrou e o
  que o acrílico custou, com criar, editar e excluir. O custo por placa é o total dividido
  pela quantidade, para baixo. Lote despachado a um pedido e sem custo é pendência, e a tela
  conta as placas vendidas do mês que ficaram sem custo.
- Revenda do grupo - a operadora marca no cadastro da revenda que ela pertence ao mesmo
  grupo. É decisão explícita, com quem marcou e quando, e a marca vale para todos os meses.
  Só as marcadas têm o DRE lido pela operadora, e só em números somados; a operadora não
  abre o razão da revenda. A revenda Avalia é marcada como qualquer outra.
- Resultado do grupo, por mês - colunas Revenda do grupo, Operadora, Eliminações e
  Consolidado.
  - Consolidado - receita reconhecida da revenda do grupo (e de nenhuma outra) mais a receita
    da operadora que vem de revendas de fora do grupo; menos comissões, o custo real de
    produção das placas, as despesas da revenda do grupo e as da operadora. Retiradas de
    sócio ficam fora do resultado.
  - Eliminações - a parte da Avalia nos serviços e o pedido de placas
    que a revenda do grupo paga à operadora saem dos dois lados. O custo da placa pelo preço
    da operadora sai da revenda, e entra o custo real de produção.
  - Custo real da placa vendida - acompanha a receita da venda, nas mesmas doze partes. A
    placa perdida, quebrada ou dada em cortesia leva o custo real no mês em que saiu.
  - Conciliação - cada transação interna é lida dos dois lados (razão da revenda do grupo e
    livro da operadora). A diferença é zero quando os dois estão completos; a tela mostra
    "Fecha em zero" ou a diferença.
  - Revenda fora do grupo - aparece só pelo que paga à operadora, na lista por revenda. O
    DRE dela nunca é lido.
- Tarifa do Pagar.me - entra como despesa mensal da operadora, categoria "Tarifa do gateway", lançada à mão com o valor do extrato.

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
- A política de privacidade fica em `/privacidade`, com link no rodapé e no cadastro; o texto espera a revisão jurídica (seção 13).

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
- Cobrança por aproximação no celular (tap to pay). Exige app nativo com o SDK
  do Pagar.me; o app do vendedor é PWA.
- Nota fiscal (NF-e e NFS-e): a plataforma ainda não emite nem coleta os dados
  para ela. Hoje ninguém emite nota ao cliente final pela plataforma, e o
  pedido de venda ao Protheus espera o serviço do Protheus. Entra quando a operação escalar.

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
- Recebedor - a revenda Avalia recebe em recebedor próprio no Pagar.me,
  separado do da conta da Avalia (seção 13). Até ele ser criado, vale o da
  conta da Avalia, numa regra só do split. O pedido de placas dela tem o
  pagamento registrado pela operadora, sem passar pelo Pagar.me.
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
| 3. Serviços | Chamados, avisos ao vivo, mapa, Worker, conexão com o Protheus, marca da revenda, serviços vendidos | Em produção; a conexão com o Protheus fica desligada até o serviço do Protheus ter endereço |
| 4. Avalia | Placas do Sales na plataforma e repasse do endereço antigo | Em produção desde 24/10/2026, com os totais conferidos; falta desligar o módulo de vendas do sistema anterior |
| 5. Encerramento | Distrato gerado pela plataforma, transferência dos clientes ativos, exportação | Em produção |

| Integração | Situação | Variáveis |
| --- | --- | --- |
| Pagar.me | Em produção desde 23/10/2026, na conta da Avalia (marketplace), com recebedor `re_` e webhook ligado; uma venda real paga e estornada. O estorno parcial real não foi executado e o ensaio do cartão em produção falta | `Pagarme__ChaveSecreta`, `Pagarme__ChavePublica`, `Pagarme__RecebedorAvalia`, `Pagarme__SegredoDoWebhook` |
| Recebedor da revenda | A operadora informa o recebedor (`re_` ou `rp_`) em Revendedores, depois de criá-lo no painel do Pagar.me. O cadastro pela API (`POST /recipients`, com KYC) ainda não existe | - |
| D4Sign | Em produção no ambiente de testes, sem validade jurídica; falta passar para a conta de produção (`secure.d4sign.com.br`) | `D4Sign__UrlBase`, `D4Sign__TokenApi`, `D4Sign__CryptKey`, `D4Sign__SegredoHmac`, `D4Sign__Cofre` |
| Resend | Em produção, com o domínio sales.avaliaone.com.br verificado | `Resend__ChaveApi`, `Resend__Remetente`, `Resend__ResponderPara` |
| Google Places | Em produção, com a chave do servidor restrita ao IP do VPS | `Google__ChaveDoServidor` |
| Google Maps no navegador | Em produção, com a chave do navegador; o Map ID próprio está a conferir (sem ele, vale `DEMO_MAP_ID`) | `Google__ChaveDoMapa`, `Google__IdDoMapa` |
| BrasilAPI | Em produção, sem chave | - |
| Gotenberg | Em produção, com o Chromium sem acesso a endereço interno ou externo | `Pdf__UrlDoConversor` |
| Protheus | Conexão REST pronta. Sem endereço configurado, a integração fica desligada e o pedido pago não entra na fila | `Protheus__UrlBase`, `Protheus__Usuario`, `Protheus__Senha` |
| Sentry | Em produção, com monitor de disponibilidade e check-in do Worker | `Sentry__Dsn` |
| SonarCloud | Projeto `Hitice_Avalia-Sales` analisado pelo CI desde 25/10/2026 (C#, TypeScript e cobertura da API), com a análise automática do painel desligada. O token vence em 24/11/2026 | `SONAR_TOKEN`, `SONAR_ORGANIZACAO`, `SONAR_PROJETO` |

## 13. Decisões de negócio

Sem decisão em aberto. A política de privacidade espera a revisão jurídica.

| Decisão | Opções | Situação |
| --- | --- | --- |
| Recebedor da revenda Avalia | Mesmo recebedor da conta do marketplace ou um separado | Decidido: separado. O extrato da revenda não se mistura com a parte da Avalia nos serviços. Falta criar o segundo recebedor no Pagar.me e informá-lo em Revendedores |
| Comissão na renovação | Renovação sempre sem comissão ou comissão de renovação ligada pela revenda | Decidido: sem comissão. A opção de ligar não existe |
| Custo real da placa no consolidado | Acompanhar a receita da venda em doze partes ou entrar inteiro no mês da venda | Decidido: doze partes, acompanhando a receita da venda, para custo e receita ficarem no mesmo período |
| Tarifa do Pagar.me no consolidado | Guardar a tarifa no razão ou ler do extrato | Decidido: despesa mensal manual da operadora, na categoria "Tarifa do gateway", lançada com o valor do extrato do Pagar.me. O razão não guarda a tarifa por pagamento |
| Termo do cliente final | Texto e assinatura do comprador ou nenhum documento | Decidido: o comprador não assina documento. O modelo do termo saiu da plataforma |
| Nota fiscal | Emitir já ou quando a operação escalar | Decidido: depois. Hoje ninguém emite nota pela plataforma |
| CNPJ da revenda Avalia | O mesmo da operadora ou outro | Decidido: outro, 39.914.870/0001-01. A operadora corrige nome e CNPJ em Revendedores. Quem recebe os clientes no encerramento é a revenda marcada como receptora, não o CNPJ |
| Política de privacidade | Texto | Texto final publicado em `/privacidade` (versão 1), a revisão jurídica ainda vai ocorrer |

## 14. Registro de mudanças

As mudanças de 21 a 24/10/2026 estão em [historico/pdd-ate-24-10.md](historico/pdd-ate-24-10.md). Daqui em diante, linha nova no topo desta tabela.

| Data | Mudança |
| --- | --- |
| 27/10/2026 | Texto alinhado ao código - ação principal da linha à vista e o resto no "⋯"; Início com o Financeiro do mês; entrada só com o logo do Pagar.me; só o revendedor renova (6.5 repetia a regra antiga); encerramento pedido em Configurações; Gotenberg sem a promessa de contêiner sem rede. |
| 27/10/2026 | Placa em branco lida sem sessão aberta - depois de entrar, o vendedor volta para a Nova venda com o código da placa preenchido. |
| 27/10/2026 | Taxa da plataforma excluída - a Avalia ganha no pedido de placas e na parte dela nos serviços; venda e renovação de placa não mandam nada à Avalia, nem pelo split nem no a acertar. Sai a coluna Taxa em Revendedores, a linha na DRE (a parte da Avalia aparece só no mês com venda de serviço), no Início e no Resultado do grupo, e o texto em Configurações. Contrato (cláusulas 1.6 e 5.3 a 5.7) e distrato (4.1 e 4.2) falam da parte da Avalia. Linha antiga da taxa soma na parte da Avalia (ADR 0003). |
| 27/10/2026 | Revenda receptora marcada pela operadora em Revendedores (selo "Receptora"), no lugar do CNPJ em configuração: sem ela o encerramento é recusado, e a receptora não encerra (ADR 0029). Fila do ERP desligada sem o endereço do Protheus: o pedido pago não entra na fila, e a tela ERP avisa. |
| 27/10/2026 | Selos de situação com até duas palavras (Carência, Descontinuado, Para retirar, Cancelada, Teste vencido, Importada, Por fora); o motivo do cancelamento fica no detalhe da venda. Recuperação de conta sem os códigos do segundo fator pela operadora, com identidade conferida fora da plataforma. |
| 27/10/2026 | Tela de entrada só com o logo do Pagar.me; tap to pay fora do escopo; correção de acerto e de domínio bloqueado é excluir e lançar de novo. |
| 27/10/2026 | Ação de todo dia à vista em toda tabela (Editar, Pagar) e a coluna de ações sempre na tela; Pedidos de placas com Cancelar também com link de pagamento aberto (a cobrança é cancelada junto); Início mostra o Financeiro do mês no lugar de um botão; Financeiro em Demonstrativo, Saídas, Caixa, Despesas e Histórico; menu lateral recolhível; rótulos de uma palavra (Aguardando, Enviando, Resultados, Bloqueados, Saídas, Hoje, Comissão, A confirmar, Repasses, Consignar, Sócios, Retiradas). Régua de texto de rótulo e de erro na seção Texto do DESIGN.md. |
| 27/10/2026 | Textos de tela sem explicação genérica: grupo da operadora, marca, clientes, caixa, repasse, configurações, segurança, placa e Nova venda ficam com o rótulo e o fato. |
| 26/10/2026 | Revendedores da operadora: a data de assinatura do contrato ganha coluna própria; textos da tela de Segurança mais curtos. |
| 26/10/2026 | Aviso por e-mail ao dono quando o segundo fator é ligado ou desligado; o item de menu da operadora passa a se chamar Bloqueados. |
| 26/10/2026 | Cada tela com uma responsabilidade, sem dado repetido: o cliente tem a casa dele em Clientes (Venda, Ficha da placa e Carteira só o apontam); a venda, no Detalhe da venda (a Ficha da placa leva a ele); o razão virou a aba Lançamentos do Financeiro; a marca virou a aba Marca de Configurações, com o encerramento da conta; a senha foi para Segurança, que abre para todos os papéis; Pedir placas só em Pedidos; o Início deixou de repetir o gráfico do Financeiro. Em Comissões, o que era "conferir" passou a "confirmar o dinheiro" que o vendedor recebeu. |
| 26/10/2026 | Fornecedores da operadora (cadastro com várias funções, ligado à despesa e ao custo de lote), transportadora e código de rastreio no despacho, estorno do pedido pago antes do despacho, lembrete de lote não ativado, a acertar das vendas recebidas por fora com registro de acerto (ADR 0028), aviso automático de vencimento da placa e e-mail de pagamento confirmado ao dono. |
| 26/10/2026 | Limite de 10 destinos novos por dia por revenda, com Solicitar liberação e liberação da operadora, e lista de destinos bloqueados que também para de redirecionar o que já existia (ADR 0026). Segundo fator exigido na conta da operadora para trocar recebedor, CNPJ e registrar dinheiro (ADR 0027), com aviso ao dono na troca de recebedor. |
| 26/10/2026 | Teste gratuito só para uma placa de avaliação no Google, uma vez por cliente e com CNPJ ou CPF e e-mail. Termo do cliente final e pacote de teste saem: o comprador não assina documento. Revenda Avalia com CNPJ próprio (39.914.870/0001-01), corrigido pela operadora em Revendedores. Nota fiscal fica fora do escopo até a operação escalar. |
| 26/10/2026 | Decisões de negócio fechadas e seção 13 sem item em aberto; registro de mudanças de 21 a 24/10 movido para `docs/historico/`. |
| 26/10/2026 | Política de privacidade publicada em `/privacidade` (versão 1), com link no rodapé da entrada e no cadastro. Modelo do termo do cliente final escrito, ainda não gerado pela venda. Categoria de despesa da operadora "Tarifa do gateway". Decididos: recebedor separado para a revenda Avalia, renovação sem comissão, custo da placa em doze partes e tarifa como despesa manual. |
| 26/10/2026 | Início, lista de Vendas e painel do vendedor contam como recebida a venda paga no sistema anterior, depois de o histórico lançar a receita dela no razão; antes, o Início mostrava só os pagamentos feitos pela plataforma e destoava do Financeiro. |
| 26/10/2026 | Página /now no celular e no tablet - o banner das telas fica entre a chamada e a descrição; no computador continua ao lado do texto. |
| 26/10/2026 | Financeiro da operadora e resultado do grupo - livro da operadora (taxa da plataforma e parte da Avalia no pagamento, pedido de placas na ativação do lote), despesas e fechamento do mês da operadora, custo de produção por lote, marca de revenda do grupo e a tela Resultado do grupo com as colunas revenda do grupo, operadora, eliminações e consolidado, conciliação e recebido ainda não reconhecido (seção 7.5, ADR 0025). |
| 26/10/2026 | A renovação da placa é anual e só do revendedor: o vendedor não a pede nem a registra por fora (a ficha e a carteira dele não oferecem Renovar, e a API recusa com 403). A renovação deixa de gastar a cota diária de recebido por fora do vendedor. |
| 26/10/2026 | A busca das listas paginadas (vendas, clientes, carteira, lotes, pedidos, chamados, links, serviços, repasses, razão, despesas, retiradas, revendedores, ERP e auditoria) volta a achar sem acento: "otica" acha "Ótica", "sao" acha "São João". |
| 25/10/2026 | Listas do painel abrem em páginas de 50 (no máximo 100), e a busca vale para a lista inteira, e não só para a página aberta. Antes, vendas, clientes, carteira, ERP e repasses cortavam em 200 ou 500 sem avisar. Os números do Início e das telas de vendas (vendas no mês, recebido, gráfico de 30 dias, comissão esperando) são calculados sobre todas as vendas, no mês de Brasília. A carteira ganha o filtro de vencimento, e o repasse só se desfaz no último de cada vendedor. Para o vendedor, a busca não olha telefone, documento nem e-mail de cliente. |
| 25/10/2026 | Cadastro público com confirmação do e-mail antes de a conta funcionar, sem senha digitada no cadastro (a senha é criada pelo link), resposta igual para e-mail e CNPJ novos ou já cadastrados, cadastro pendente descartado em 48 horas, captcha Turnstile (desligado até ligar) e limites por IP, e-mail, CNPJ e dia. |
| 25/10/2026 | Repetir um envio (toque duplo, rede ruim) não duplica mais venda, despesa, pedido nem repasse; reenviar o acesso por rede ruim não desloga mais o usuário (janela de 10 s); cupom e repasse com data em outro fuso deixam de dar erro. |
| 25/10/2026 | Cliente alheio mascarado para o vendedor - documento, e-mail e telefone completos só do cliente que ele cadastrou; dos demais, nome e contato mascarado (CPF `***.456.789-**`, e-mail `m***@dominio.com`, telefone `(11) *****-7777`) no detalhe da venda, na ficha da placa e na carteira. O botão de WhatsApp some quando o telefone está mascarado. Links do vendedor: só os dos clientes das vendas dele e os que ele criou. |
| 25/10/2026 | Recebido por fora do vendedor com garantia - aviso imediato ao revendedor com o atalho para desfazer, limite por dia (valor e quantidade) e teto por venda (padrão R$ 10.000,00, 30 recebimentos e R$ 5.000,00, ajustados em Equipe) e conferência do revendedor, em Comissões, antes de a comissão entrar num repasse. O que o revendedor registra já nasce conferido. |
| 25/10/2026 | Mapa cria o Google Maps uma vez e busca cada lugar no Places uma vez só. |
| 25/10/2026 | Revisão do front - os dados de uma revenda não aparecem mais para quem entra depois no mesmo navegador; toque duplo em gravar não duplica venda, despesa nem pedido; Financeiro, ficha da placa e clientes se atualizam depois de cancelar, receber, repassar e dos avisos ao vivo; o login não cai quando a renovação do acesso fica indisponível por um momento; o diálogo devolve o foco ao fechar; a validade do cupom e as datas seguem o horário de Brasília; com data limite, o repasse não mostra o valor no botão. |
| 25/10/2026 | Entrada e links - "Esqueci a senha" envia no máximo um e-mail por minuto por conta; destino com acento é gravado codificado e quebra de linha é recusada, para a placa impressa nunca responder erro; dois links não nascem mais para o mesmo destino em pedidos simultâneos. |
| 25/10/2026 | Dinheiro sob repetição - pagar o pedido de placas, receber por fora e cancelar a venda de um grupo mais de uma vez ao mesmo tempo geram um efeito só (um link, um recebimento, uma devolução ao cliente). O repasse com data de corte leva junto o estorno da comissão, e a venda cancelada depois do corte não é repassada. Estorno pelo painel do Pagar.me, sem webhook, passa a ser reconhecido na reconsulta diária. |
| 25/10/2026 | Ficha da placa - o vendedor lê a ficha completa da placa em estoque (para conferir na Nova venda) e da placa que ele vendeu. Placa vendida por outro vendedor mostra só o código e o estado; cliente, valor, comissão, destino e histórico são de quem vendeu e do revendedor. |
| 25/10/2026 | Renovação recebida por fora pede confirmação na ficha da placa e vai à auditoria; o revendedor desfaz a renovação registrada por engano (estorno no razão e o vencimento de antes). |
| 25/10/2026 | Histórico das vendas do Sales - a receita e a comissão das vendas pagas no sistema anterior entram no razão nos meses de origem (receita e comissão devida no mês do pagamento, comissão paga no mês em que foi paga, sem taxa da Avalia); mês fechado recusa a importação; o saldo do vendedor fica só com o que falta pagar, e o repasse não conta a venda paga lá. |
| 25/10/2026 | Financeiro - cartão Caixa com o saldo, o saldo inicial, as entradas e as saídas; o revendedor define, troca ou exclui o saldo inicial pelo cartão. Aporte de sócio na lista de naturezas da retirada. |
| 25/10/2026 | Tabelas no celular - os cartões correm com a página, sem barra de rolagem por cima do texto; a busca fica em cima e a primeira ação do cartão vira botão. |
| 25/10/2026 | Tabelas no computador - colunas alinhadas pelo tipo (número à direita, selos da mesma largura) e toda ação da linha no "⋯" da última coluna. Despachar pedido, editar e suspender revendedor passam a abrir em modal. |
| 25/10/2026 | Caixa da revenda a partir do saldo inicial do mês (o capital de giro), com aporte de sócio; a venda trazida do sistema anterior é reconhecida inteira no mês dela. |
| 25/10/2026 | Tela de entrada com os logos oficiais do Pagar.me e da Stone, nas cores das marcas; o Resend sai. |
| 25/10/2026 | Mapa abre mais perto - com oito lugares ou mais, o enquadramento fica com os 80% do meio, e o lugar isolado na ponta da cidade aparece ao arrastar; zoom até o nível da rua. |
| 25/10/2026 | Documento revisto - texto reorganizado, estado das integrações e das fases atualizado. |
