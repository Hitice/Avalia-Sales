import type { AcaoAuditada, CategoriaDeDespesa, CategoriaDeDespesaDaOperadora, MeioDePagamento, TipoDeLancamento, CategoriaDoLote, EstadoDaPlaca, MotivoDaBaixa, MotivoDoCancelamento, Pedido, SituacaoDaCobranca, SituacaoDaOrdem, SituacaoDoContrato, SituacaoDoPedido, SituacaoDoProduto, NaturezaDaRetirada, TipoDeProduto } from './tipos'
import { FUSO_DE_BRASILIA } from '@/lib/data'

/** Os nomes que a tela mostra para cada situação da API. */
export const ROTULO_DO_ESTADO: Record<EstadoDaPlaca, string> = {
  EmEstoque: 'Em estoque',
  Ativa: 'Ativa',
  Carencia: 'Carência',
  Vencida: 'Vencida',
  Suspensa: 'Suspensa',
  Baixada: 'Encerrada',
}

export const ROTULO_DA_CATEGORIA: Record<CategoriaDoLote, string> = {
  Placa: 'Placa',
  Cracha: 'Crachá',
  Cardapio: 'Cardápio',
  Teste: 'Teste',
  Outro: 'Avulso',
}

/** Só os motivos que o dono escolhe; troca, lote arquivado e encerramento vêm do sistema. */
export const ROTULO_DA_BAIXA: Partial<Record<MotivoDaBaixa, string>> = {
  DefeitoDeImpressao: 'Defeito de impressão',
  Quebrada: 'Quebrada',
  Perdida: 'Perdida',
  NaoImpressa: 'Não impressa',
}

export const ROTULO_DO_CANCELAMENTO: Record<MotivoDoCancelamento, string> = {
  ErroDeCadastro: 'erro de cadastro',
  Inadimplencia: 'não pagou',
  Desistencia: 'desistência',
  Defeito: 'defeito',
  Outro: 'outro motivo',
}

export const ROTULO_DO_PEDIDO: Record<SituacaoDoPedido, string> = {
  AguardandoPagamento: 'Aguardando',
  Pago: 'Pago',
  EmProducao: 'Em produção',
  Despachado: 'Enviado',
  Ativado: 'Entregue',
  Cancelado: 'Cancelado',
}

/** O pedido despachado para retirada espera a revenda buscar a caixa na Avalia. */
export const rotuloDoPedido = (p: Pick<Pedido, 'situacao' | 'retirada'>) =>
  p.situacao === 'Despachado' && p.retirada ? 'Para retirar' : ROTULO_DO_PEDIDO[p.situacao]

export const ROTULO_DO_LANCAMENTO: Record<TipoDeLancamento, string> = {
  Receita: 'Receita',
  TaxaAvalia: 'Parte da Avalia',
  ComissaoDevida: 'Comissão devida',
  ComissaoPaga: 'Comissão repassada',
  CustoDaPlaca: 'Custo da placa',
  Perda: 'Perda de placa',
  Despesa: 'Despesa',
  RetiradaDeSocio: 'Retirada de sócio',
  AporteDeSocio: 'Aporte de sócio',
  SaldoInicialDoCaixa: 'Saldo inicial do caixa',
}

export const ROTULO_DO_MEIO: Record<MeioDePagamento, string> = {
  Pix: 'Pix',
  Cartao: 'Cartão',
  Link: 'Link de pagamento',
  Dinheiro: 'Dinheiro',
  OutroPix: 'Outro Pix',
}

export const ROTULO_DA_ORDEM: Record<SituacaoDaOrdem, string> = {
  AFazer: 'A fazer',
  EmAndamento: 'Em andamento',
  Entregue: 'Entregue',
  Cancelada: 'Cancelada',
}

export const ROTULO_DO_PRODUTO: Record<SituacaoDoProduto, string> = {
  Disponivel: 'Disponível',
  Esgotado: 'Esgotado',
  ForaDeLinha: 'Descontinuado',
}

export const ROTULO_DO_TIPO: Record<TipoDeProduto, string> = {
  Placa: 'Placa',
  PlacaComServico: 'Placa com serviço',
  Servico: 'Serviço',
}

export const ROTULO_DO_PAGAMENTO: Record<SituacaoDaCobranca, string> = {
  Pendente: 'Aguardando',
  Paga: 'Paga',
  Cancelada: 'Cancelada',
  Falhou: 'Falhou',
  Estornada: 'Estornada',
}

export const ROTULO_DO_CONTRATO: Record<SituacaoDoContrato, string> = {
  Rascunho: 'Enviando',
  AguardandoAssinatura: 'Aguardando',
  Assinado: 'Assinado',
  Cancelado: 'Cancelado',
}

/**
 * O tom do selo de cada situação (DESIGN.md): verde quando está resolvido a
 * favor (pago, entregue, no ar), âmbar quando espera alguém, vermelho quando
 * falhou ou acabou mal, azul quando anda sozinho, neutro no resto.
 */
export type Tom = 'sucesso' | 'atencao' | 'erro' | 'info' | 'neutro'

export const TOM_DO_ESTADO: Record<EstadoDaPlaca, Tom> = {
  EmEstoque: 'neutro',
  Ativa: 'sucesso',
  Carencia: 'atencao',
  Vencida: 'erro',
  Suspensa: 'atencao',
  Baixada: 'neutro',
}

export const TOM_DO_PEDIDO: Record<SituacaoDoPedido, Tom> = {
  AguardandoPagamento: 'atencao',
  Pago: 'info',
  EmProducao: 'info',
  Despachado: 'info',
  Ativado: 'sucesso',
  Cancelado: 'erro',
}

export const TOM_DA_ORDEM: Record<SituacaoDaOrdem, Tom> = {
  AFazer: 'atencao',
  EmAndamento: 'info',
  Entregue: 'sucesso',
  Cancelada: 'erro',
}

export const TOM_DO_PRODUTO: Record<SituacaoDoProduto, Tom> = {
  Disponivel: 'sucesso',
  Esgotado: 'atencao',
  ForaDeLinha: 'neutro',
}

export const TOM_DO_PAGAMENTO: Record<SituacaoDaCobranca, Tom> = {
  Pendente: 'atencao',
  Paga: 'sucesso',
  Cancelada: 'erro',
  Falhou: 'erro',
  Estornada: 'neutro',
}

export const TOM_DO_CONTRATO: Record<SituacaoDoContrato, Tom> = {
  Rascunho: 'neutro',
  AguardandoAssinatura: 'atencao',
  Assinado: 'sucesso',
  Cancelado: 'erro',
}

// Em Brasília, como a competência e o fim do dia da API: o fuso do navegador não muda o dia mostrado.
const data = new Intl.DateTimeFormat('pt-BR', { timeZone: FUSO_DE_BRASILIA, day: '2-digit', month: '2-digit', year: 'numeric' })
const dataHora = new Intl.DateTimeFormat('pt-BR', { timeZone: FUSO_DE_BRASILIA, day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' })

export const formatarData = (iso: string | null) => (iso ? data.format(new Date(iso)) : '-')
export const formatarDataHora = (iso: string | null) => (iso ? dataHora.format(new Date(iso)) : '-')

/** 2000 pontos base viram "20%"; 2550 viram "25,5%". */
export const formatarBps = (bps: number) => `${(bps / 100).toLocaleString('pt-BR', { maximumFractionDigits: 2 })}%`

/** "20", "20,5" ou "20%" viram pontos base; null se não for um percentual de 0 a 100. */
export function paraBps(texto: string): number | null {
  const limpo = texto.replaceAll('%', '').trim().replace(',', '.')
  if (!/^\d{1,3}(\.\d{1,2})?$/.test(limpo)) return null
  const [inteiro, fracao = ''] = limpo.split('.')
  const bps = Number(inteiro) * 100 + Number(fracao.padEnd(2, '0'))
  return bps <= 10_000 ? bps : null
}

/** 11977771234 vira (11) 97777-1234; com o 55 na frente, +55 (11) 97777-1234. */
export function formatarTelefone(digitos: string | null): string {
  if (!digitos) return '-'
  const pais = digitos.length > 11 ? `+${digitos.slice(0, digitos.length - 11)} ` : ''
  const local = digitos.slice(-11)
  const m = /^(\d{2})(\d{4,5})(\d{4})$/.exec(local.length === 11 ? local : digitos.slice(-10))
  return m ? `${pais}(${m[1]}) ${m[2]}-${m[3]}` : digitos
}

/** O vendedor lê só o final do telefone do cliente que outro cadastrou: não há número para ligar nem para o WhatsApp. */
export const telefoneMascarado = (telefone: string | null) => !!telefone && telefone.includes('*')

export const ROTULO_DA_ACAO: Record<AcaoAuditada, string> = {
  DestinoDefinido: 'Destino definido na venda',
  DestinoTrocado: 'Destino trocado',
  PlacaSuspensa: 'Placa suspensa',
  PlacaNoAr: 'Placa de volta ao ar',
  PlacaTrocada: 'Placa trocada',
  VendaCancelada: 'Venda cancelada',
  VendaEmCortesia: 'Venda virou cortesia',
  RecebidoPorFora: 'Recebido por fora',
  RecebimentoDesfeito: 'Recebimento desfeito',
  VendedorCadastrado: 'Vendedor cadastrado',
  VendedorAlterado: 'Vendedor alterado',
  VendedorDesativado: 'Vendedor desativado',
  VendedorReativado: 'Vendedor reativado',
  VendedorExcluido: 'Vendedor excluído',
  ComissaoPadraoAlterada: 'Comissão padrão alterada',
  ProdutoAlterado: 'Produto alterado',
  RepasseRegistrado: 'Repasse registrado',
  ClienteAnonimizado: 'Cliente anonimizado',
  MarcaAlterada: 'Marca alterada',
  LinkAlterado: 'Link alterado',
  LinkExcluido: 'Link excluído',
  SegundoFatorLigado: 'Segundo fator ligado',
  SegundoFatorDesligado: 'Segundo fator desligado',
  EmpresaAlterada: 'Dados da empresa alterados',
  EncerramentoPedido: 'Encerramento pedido',
  EncerramentoDesistido: 'Encerramento desistido',
  ContaEncerrada: 'Conta encerrada',
  ReaberturaPedida: 'Reabertura pedida',
  ClienteAlterado: 'Cliente alterado',
  ClienteExcluido: 'Cliente excluído',
  ClientesJuntados: 'Clientes juntados',
  VendaCorrigida: 'Venda corrigida',
  RepasseDesfeito: 'Repasse desfeito',
  MesFechado: 'Mês fechado',
  MesReaberto: 'Mês reaberto',
  SocioCadastrado: 'Sócio cadastrado',
  SocioAlterado: 'Sócio alterado',
  SocioExcluido: 'Sócio excluído',
  ClienteCadastrado: 'Cliente cadastrado',
  SaldoInicialDoCaixaDefinido: 'Saldo inicial do caixa definido',
  SaldoInicialDoCaixaExcluido: 'Saldo inicial do caixa excluído',
  RenovacaoRecebidaPorFora: 'Renovação recebida por fora',
  RenovacaoDesfeita: 'Renovação desfeita',
  LinkCriado: 'Link criado',
  RecebimentoConferido: 'Recebimento por fora conferido',
  LimiteDePorForaDefinido: 'Limite de recebimento por fora definido',
}

export const ROTULO_DA_DESPESA: Record<CategoriaDeDespesa, string> = {
  PlacasEMaterial: 'Placas e material',
  Frete: 'Frete',
  Marketing: 'Marketing',
  SoftwareEFerramentas: 'Software e ferramentas',
  Impostos: 'Impostos',
  Taxas: 'Taxas',
  Outros: 'Outros',
  ServicoDePjComNota: 'Serviço de PJ com nota',
}

export const ROTULO_DA_DESPESA_DA_OPERADORA: Record<CategoriaDeDespesaDaOperadora, string> = {
  GraficaEMaterial: 'Gráfica e material',
  ExecucaoDeServico: 'Execução de serviço',
  Frete: 'Frete',
  SoftwareEFerramentas: 'Software e ferramentas',
  Impostos: 'Impostos',
  Outros: 'Outros',
  TarifaDoGateway: 'Tarifa do gateway',
}

export const ROTULO_DA_RETIRADA: Record<NaturezaDaRetirada, string> = {
  ProLabore: 'Pró-labore',
  DistribuicaoDeLucros: 'Distribuição de lucros',
  Adiantamento: 'Adiantamento',
  Aporte: 'Aporte',
}
