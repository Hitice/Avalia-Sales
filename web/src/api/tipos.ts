/** Os formatos que a API devolve, com os mesmos nomes dela. Dinheiro sempre em centavos. */

export type EstadoDaPlaca = 'EmEstoque' | 'Ativa' | 'Carencia' | 'Vencida' | 'Suspensa' | 'Baixada'
export type SituacaoDoPedido = 'AguardandoPagamento' | 'Pago' | 'Despachado' | 'Ativado' | 'Cancelado'
export type SituacaoDaCobranca = 'Pendente' | 'Paga' | 'Cancelada' | 'Falhou' | 'Estornada'
export type SituacaoDoContrato = 'Rascunho' | 'AguardandoAssinatura' | 'Assinado' | 'Cancelado'

export type Pacote = { id: string; quantidade: number; precoCents: number; custoPorPlacaCents: number; ativo: boolean }

export type Pedido = {
  id: string
  revendedorId: string
  revendedor: string
  quantidade: number
  valorCents: number
  situacao: SituacaoDoPedido
  criadoEm: string
  pagoEm: string | null
  lote: string | null
  despachadoEm: string | null
  ativadoEm: string | null
  precoDoPacoteCents: number
  cupom: string | null
  descontoCents: number
  urlDePagamento: string | null
}

export type Lote = {
  id: string
  rotulo: string
  numero: number
  quantidade: number
  criadoEm: string
  revendedor: string | null
  despachadoEm: string | null
  ativadoEm: string | null
}

export type Revendedor = {
  id: string
  nome: string
  cnpj: string | null
  taxaAvaliaBps: number
  comissaoPadraoBps: number
  recebedorPagarme: string | null
  contratoAssinadoEm: string | null
  criadoEm: string
  renovacaoBps: number
}

export type MinhaRevenda = { revendedor: Revendedor; contrato: SituacaoDoContrato | null }

export type PlacaDaLista = {
  codigo: string
  sequencia: number
  estado: EstadoDaPlaca
  consignadaA: string | null
  consignadaANome: string | null
  destino: string | null
  vendidaEm: string | null
  venceEm: string | null
  totalLeituras: number
  ultimaLeituraEm: string | null
}

export type PaginaDePlacas = { placas: PlacaDaLista[]; total: number; porEstado: Partial<Record<EstadoDaPlaca, number>> }

export type Vendedor = {
  id: string
  nome: string
  email: string
  telefone: string | null
  chavePix: string | null
  comissaoBps: number | null
  comissaoEfetivaBps: number
  ativo: boolean
  vendas: number
}

export type VendaDaLista = {
  id: string
  codigo: string
  cliente: string
  produto: string
  vendedorId: string
  vendedor: string
  valorCents: number
  comissaoCents: number
  meio: 'Pix' | 'Cartao'
  pagamento: SituacaoDaCobranca | null
  vendidaEm: string
  pagaEm: string | null
}

export type Cobranca = {
  id: string
  situacao: SituacaoDaCobranca
  valorCents: number
  taxaAvaliaCents: number
  pixCopiaECola: string | null
  pixQrCodeUrl: string | null
  pixExpiraEm: string | null
  urlDoLink: string | null
}

export type VendaRegistrada = { vendaId: string; codigo: string; endereco: string; valorCents: number; venceEm: string | null; cobranca: Cobranca | null; semCobranca: string | null }

export type Ficha = {
  endereco: string
  codigo: string
  sequencia: number
  estado: EstadoDaPlaca
  destino: string | null
  venceEm: string | null
  fimDaCarencia: string | null
  totalLeituras: number
  ultimaLeituraEm: string | null
  consignadaA: string | null
  venda: {
    id: string
    cliente: string
    responsavel: string
    telefone: string
    produto: string
    vendedor: string
    valorCents: number
    comissaoCents: number
    pagamento: SituacaoDaCobranca | null
    vendidaEm: string
  } | null
  leituras: { dia: string; total: number }[]
}

export type Lugar = { id: string; nome: string; endereco: string | null }

export type TipoDeDestino = 'Google' | 'Link'
export type DestinoInformado = { tipo: TipoDeDestino; lugarNoGoogle: string | null; link: string | null }

export type Cupom = { id: string; codigo: string; descontoBps: number | null; descontoCents: number | null; validoAte: string | null; usosMaximos: number | null; usos: number; ativo: boolean }

export type SaldoDeComissao = { vendedorId: string; vendedor: string; chavePix: string | null; devidaCents: number; repassadaCents: number; abertaCents: number; ultimoRepasse: string | null }

export type Repasse = { id: string; vendedorId: string; vendedor: string; valorCents: number; vendas: number; ateVendaEm: string; pagoEm: string; comprovante: string | null }

export type TipoDeLancamento = 'Receita' | 'TaxaAvalia' | 'ComissaoDevida' | 'ComissaoPaga'

export type ResumoDoMes = {
  competencia: string
  receitaCents: number
  taxaAvaliaCents: number
  comissaoCents: number
  liquidoCents: number
  lancamentos: { id: string; tipo: TipoDeLancamento; valorCents: number; descricao: string; ocorridoEm: string; competencia: string; estorno: boolean }[]
}

export type RenovacaoAberta = { id: string; codigo: string; valorCents: number; venceEm: string | null; cobranca: Cobranca | null; semCobranca: string | null }
