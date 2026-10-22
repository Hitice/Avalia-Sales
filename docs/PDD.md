# Avalia Sales - documento de produto (PDD)

O que a plataforma faz, para quem, e as regras de negócio. O como fica no
[SDD](SDD.md). Os dois são atualizados no mesmo commit da mudança que os
afeta, com uma linha no registro de mudanças do fim.

Situação em 21/10/2026 - Fase 0 pronta, integrações com Pagar.me e D4Sign no
código, e a Fase 1 com API e telas: cadastro, pedido, despacho e ativação de
lote, estoque, consignação, equipe, catálogo, Nova venda com QR, NFC e Pix, e
ficha da placa. Falta para fechar a Fase 1 rodar os sandboxes reais e a
revisão do texto do contrato.

## 1. Visão

Plataforma para revender placas com QR Code e NFC que levam o cliente a uma
avaliação no Google, a um link ou a uma página de links. A Avalia opera a
plataforma e vende as placas aos revendedores. Cada revendedor vende com a
própria equipe, recebe pelo Pagar.me e acompanha as vendas num painel.

## 2. Papéis

| Papel | Quem é | O que faz |
| --- | --- | --- |
| Operadora | A Avalia | Vende placas aos revendedores, recebe a taxa de cada venda, despacha lotes, atende o chat |
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

- Taxa da Avalia - 1,99% de cada venda paga, gravada na cobrança no momento
  dela. A confirmar se é 1,99% ou R$ 1,99 fixo.
- Pacotes de placas - configuráveis pela operadora - 20, 50 e 100, e um de 10
  para o teste da revendedora Avalia. Preço a definir.
- Taxa do Pagar.me - paga pelo revendedor.
- Exemplo - venda de R$ 100,00 com comissão de 20%: R$ 1,99 para a Avalia,
  R$ 98,01 para o revendedor (de onde sai a taxa do Pagar.me), e R$ 20,00
  lançados como devidos ao vendedor.

## 4. Jornada do revendedor

1. Cadastro - pela tela, com empresa, CNPJ e o login do dono. A conta para
   receber no Pagar.me é informada pela operadora até o cadastro de recebedor
   pela API entrar.
2. Contrato - criado sozinho depois do cadastro e enviado pela D4Sign ao e-mail
   do dono. Sem contrato assinado, não há pedido. Contrato assinado em papel é
   registrado pela operadora.
3. Pedido - pacote de placas, com cupom opcional, pago antes do envio pelo
   checkout do Pagar.me (Pix, cartão ou boleto, tudo para a Avalia). A
   operadora também pode registrar um pagamento recebido por fora.
4. Envio - a Avalia recebe o pedido por e-mail e na fila, e despacha um lote da
   mesma quantidade; o dono recebe o e-mail com o número do lote.
5. Ativação - o revendedor digita o número do lote (LT-0001, lt 1 ou 1); as
   placas entram no estoque dele com o custo do pacote.
6. Equipe - cadastro dos vendedores e da comissão de cada um.
7. Venda - o vendedor busca o negócio no Google (ou cola o link de avaliação do perfil, ou o link do cliente), lê o QR da placa ou digita o código, preenche o cliente e cobra. No fim grava na tag NFC o mesmo endereço do QR (`/Q/{codigo}`).
8. Venda - cliente, negócio, destino do QR e cobrança por Pix ou cartão.
9. Pagamento - o cliente paga e o split divide na hora.
10. Painel - vendas ao vivo, saldo de comissão e mapa dos lugares vendidos.

## 5. Telas

Todas no mesmo app, liberadas pelo papel do usuário.

| Tela | Quem usa | Pensada para | O que faz |
| --- | --- | --- | --- |
| Cadastro e contrato | Revendedor | Computador | Dados da empresa, conta para receber, contrato |
| Pedido de placas | Revendedor | Computador | Pacote, pagamento, acompanhamento do envio |
| Ativar lote | Revendedor | Computador | Número do lote recebido |
| Vendedores | Revendedor | Computador | Criar, editar, desativar, excluir, comissão |
| Catálogo | Revendedor | Computador e celular | Produtos e preço da tabela do revendedor |
| Marca | Revendedor | Computador | Logo, cores e nome exibido, com prévia |
| Painel | Revendedor | Computador | Vendas e pagamentos ao vivo, saldo de comissão, registro de repasse |
| Auditoria | Revendedor | Computador | Quem mudou o quê, com antes e depois e busca pelo código |
| Segurança | Dono e operadora | Celular e computador | Ligar e desligar o segundo fator (app autenticador), com códigos de recuperação |
| Mapa | Revendedor | Computador | Lugares vendidos, no Google Maps |
| Painel do vendedor | Vendedor | Celular | Vendas do mês, comissão a receber, esperando pagamento e recebida, placas com ele |
| Nova venda | Vendedor | Celular | Negócio no Google, QR da placa, cliente, cobrança, gravação do NFC |
| Links | Dono e vendedor | Celular | Link curto do negócio sem placa: copiar, gravar NFC; o dono edita, desliga e exclui |
| Ficha da placa | Vendedor e revendedor | Celular | Destino, vencimento, leituras, troca, renovação |
| Pedidos | Operadora | Computador | Pedidos pagos, lote despachado, envio |
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

### Link curto

- `/L/{codigo}` - mesmo formato do código da placa (6 caracteres Crockford),
  cerca de 33 bytes; cabe em qualquer tag NFC.
- A venda com destino no Google cria o link do negócio, ou reaproveita o que já
  leva ao mesmo destino. A tela Links cria o do negócio sem placa.
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
- Não há consignação entre a Avalia e o revendedor - o revendedor paga o
  pacote antes e as placas ativadas são dele. Consignação só existe dentro do
  revendedor, do estoque dele para um vendedor da equipe, e de volta.
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

- O dono fala com a Avalia pelo chat da plataforma (Falar com a Avalia); a
  operadora atende todas as conversas, com as não lidas primeiro. Mensagem não
  é editada nem apagada. Abrir a conversa marca como lidas as mensagens do
  outro lado, e a mensagem nova chega ao vivo.

### Avisos ao vivo

- Venda, pagamento, pedido, estoque, repasse e ERP atualizam as telas abertas
  sem recarregar, com um aviso curto. Cada revendedor só recebe os próprios.

### Contrato

- O contrato do revendedor e o termo do cliente são assinados na D4Sign.
- O PDF original e o hash SHA-256 dele ficam guardados, para provar qual texto
  foi assinado.
- Contrato só anda para a frente - aguardando assinatura vira assinado ou
  cancelado.
- O contrato de revenda e os avisos ao dono só vão para e-mail confirmado. O
  cadastro manda o link de confirmação (vale 2 dias); o painel lembra e
  reenvia. Conta criada pela operadora já nasce confirmada.

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
| Mapa de bairros em Leaflet com OpenStreetMap | Google Maps JavaScript | Fase 3 |
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
- A definir com o jurídico - prazo de guarda depois do fim do contrato do
  revendedor e o texto da política de privacidade.

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
marca da plataforma apaga cores, nome e logo. O e-mail com a marca do
revendedor fica para depois.

## 8. Fora do escopo

- Avalia Gestor, Lia, página de links e loja, que seguem no sistema atual da
  Avalia.
- WhatsApp e atendimento por IA. O pré-atendimento é o chat do revendedor com
  a Avalia, dentro da plataforma.
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

Ferramentas prontas para a Fase 4 (docs/OPERACAO.md) - reservar os códigos
do Sales antes de qualquer lote de produção, e importar as placas por um
arquivo que serve a qualquer revendedor, com ensaio que só mostra os totais. A
venda importada mantém o valor de origem, sem cobrança e sem comissão.

Regra que não quebra - `avaliaone.com.br/q/{codigo}` responde para sempre,
porque está impresso nas placas vendidas.

## 10. Fases

Estimativa em semanas de cerca de 20 horas.

| Fase | Conteúdo | Semanas | Situação |
| --- | --- | --- | --- |
| 0. Base | Solução em camadas, login, isolamento entre revendedores, CI | 2 | Feita |
| Integrações | Pagar.me (cobrança, split, webhook) e D4Sign (envio, webhook) no código | - | Feita, falta sandbox real |
| 1. Revenda | Cadastro, contrato, pedido, lote, equipe, venda, NFC e QR | 3 | Feita em simulação; falta o sandbox real do Pagar.me e da D4Sign |
| 2. Pagamento | Checkout do pedido de placas, cupom, comissão, razão | 3 | Feita em simulação; falta o sandbox real |
| 3. Serviços | Chat, painel ao vivo, mapa, Worker, Protheus simulado, white label | 4 | Feita; o mapa com o Google real espera a chave do navegador |
| 4. Avalia | Teste, integração com o Sales, migração e volta | 3 | Ferramentas prontas (reservar códigos, importar placas com ensaio); falta a virada com o Sales |

## 11. Decisões pendentes

| Decisão | Opções | Recomendação |
| --- | --- | --- |
| Taxa da Avalia | 1,99% ou R$ 1,99 fixo por venda | 1,99% (o código assume) |
| Preço dos pacotes | Por pacote | A definir; a demonstração usa R$ 150 (10), R$ 280 (20), R$ 650 (50) e R$ 1.200 (100), simulados |
| Confirmação de pagamento por fora | Baixa manual no Pagar.me ou só registro na plataforma | Conferir a API do Pagar.me na Fase 2 |
| Nome e domínio | Subdomínio de avaliaone.com.br ou domínio próprio | A definir |
| Recebedor da revendedora Avalia | Mesmo da conta do marketplace ou separado | Confirmar com o Pagar.me |
| Textos do contrato e do termo | Rascunho ou revisão jurídica | Revisão antes do fim da Fase 1; hoje vai uma minuta marcada como homologação |
| Repositório | Público ou privado | Público, com dados fictícios |

### Fase das APIs (com as contas e chaves)

O que está pronto no código e só fecha com a conta de verdade:

| Integração | O que falta | Variáveis |
| --- | --- | --- |
| Pagar.me | Sandbox: Pix, cartão (formato do `/tokens` com a chave pública), split, webhook; confirmar PSP com split na conta | `Pagarme__ChaveSecreta`, `Pagarme__ChavePublica`, `Pagarme__RecebedorAvalia`, `Pagarme__SegredoDoWebhook` |
| Recebedor do revendedor | Criar pela API (`POST /recipients` com `register_information` e KYC). A referência de 2026 se contradiz no prefixo do id (`re_` ou `rp_`), nos tipos (texto ou número) e no formato da data; escrever com o sandbox. Hoje a operadora informa o `rp_` à mão, e a configuração só aceita `rp_` | - |
| D4Sign | Sandbox: envio, assinatura e webhook do contrato | `D4Sign__TokenApi`, `D4Sign__CryptKey`, `D4Sign__SegredoHmac`, `D4Sign__Cofre`, `D4Sign__UrlDoWebhook` |
| Resend | Domínio verificado (SPF, DKIM, DMARC); remetente e resposta | `Resend__ChaveApi`, `Resend__Remetente`, `Resend__ResponderPara` |
| Google | Chave do servidor (Places New) e do mapa (Maps JavaScript, restrita ao domínio) e o Map ID | `Google__ChaveDoServidor`, `Google__ChaveDoMapa`, `Google__IdDoMapa` |
| Sentry e SonarCloud | DSN e token | `Sentry__Dsn`, `SONAR_TOKEN` e as variáveis do projeto |
| Servidor | VPS, DNS, runner e `DEPLOY_ATIVO`; conferir a CSP com `curl -sI` no primeiro deploy | - |

## 12. Registro de mudanças

| Data | Mudança |
| --- | --- |
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
