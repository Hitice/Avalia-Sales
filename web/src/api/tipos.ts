import type { components } from './esquema'

/**
 * Os formatos da API, gerados do contrato (web/openapi.json, `npm run tipos`)
 * e apelidados com os nomes que as telas usam. Tipo escrito à mão aqui só
 * para o que o contrato não descreve. Dinheiro sempre em centavos.
 */
type Esquema = components['schemas']

/** O envelope de toda lista paginada pela API: os itens da página, o total do filtro e a próxima página (nula na última). */
export type Pagina<T> = { itens: T[]; total: number; proxima: number | null }

export type EstadoDaPlaca = Esquema['EstadoDaPlaca']
export type SituacaoDoPedido = Esquema['SituacaoDoPedido']
// O gerador do .NET põe null na enumeração quando algum DTO a usa como opcional; o nulo fica no campo.
export type SituacaoDaCobranca = NonNullable<Esquema['SituacaoDaCobranca']>
export type SituacaoDoContrato = NonNullable<Esquema['SituacaoDoContrato']>
export type TipoDeLancamento = Esquema['TipoDeLancamento']

export type Pacote = Esquema['PacoteDto']
export type Pedido = Esquema['PedidoDto']
export type Lote = Esquema['LoteDto']
export type CategoriaDoLote = Esquema['CategoriaDoLote']
export type LoteDaRevenda = Esquema['LoteDaRevenda']
export type MotivoDaBaixa = Esquema['MotivoDaBaixa']
export type MotivoDoCancelamento = NonNullable<Esquema['MotivoDoCancelamento']>
export type PlacaDaVendaCancelada = Esquema['PlacaDaVendaCancelada']
export type Revendedor = Esquema['RevendedorDto']
export type MinhaRevenda = Esquema['MinhaRevenda']
export type PaginaDePlacas = Omit<Esquema['PaginaDePlacas'], 'porEstado'> & { porEstado: Partial<Record<EstadoDaPlaca, number>> }
export type Vendedor = Esquema['VendedorDto']
export type VendaDaLista = Esquema['VendaDaLista']
export type DetalheDaVenda = Esquema['DetalheDaVenda']
export type ResumoDoVendedor = Esquema['ResumoDoVendedor']
export type PlacaDaCarteira = Esquema['PlacaDaCarteira']
export type MeioDePagamento = Esquema['MeioDePagamento']
export type VendaRegistrada = Esquema['VendaRegistrada']
export type NovaVendaComItens = Esquema['NovaVendaComItens']
/** A resposta do POST /vendas/itens. As vendas vêm na ordem dos itens enviados. */
export type VendasRegistradas = Esquema['VendasRegistradas']
export type LinkCurto = Esquema['LinkDto']
export type Ficha = Esquema['FichaDto']
export type Lugar = Esquema['Lugar']
export type FichaDoCliente = Esquema['FichaDoCliente']
export type ClienteDaLista = Esquema['ClienteDaLista']
export type DestinoInformado = Esquema['DestinoInformado']
export type Chamado = Esquema['ChamadoDto']
export type ItemDoCatalogo = Esquema['ItemDoCatalogo']
export type ProdutoDaPlataforma = Esquema['ProdutoDaPlataformaDto']
export type TipoDeProduto = Esquema['TipoDeProduto']
export type SituacaoDoProduto = Esquema['SituacaoDoProduto']
export type OrdemDeServico = Esquema['OrdemDto']
export type SituacaoDaOrdem = Esquema['SituacaoDaOrdem']
export type CaixaDeNotificacoes = Esquema['CaixaDeNotificacoes']
export type ChamadoComMensagens = Esquema['ChamadoComMensagens']
export type MensagemDoChamado = Esquema['MensagemDto']
export type SituacaoDoChamado = Esquema['SituacaoDoChamado']
export type ResumoDaFila = Esquema['ResumoDaFila']
export type Cupom = Esquema['CupomDto']
export type SaldoDeComissao = Esquema['SaldoDeComissao']
export type PendenteDeConferencia = Esquema['PendenteDeConferencia']
export type LimitesDePorFora = Esquema['LimitesDePorFora']
export type Repasse = Esquema['RepasseDto']
export type ResumoDoMes = Esquema['ResumoDoMes']
export type RenovacaoAberta = Esquema['RenovacaoAberta']
export type EnvioAoErp = Esquema['EnvioDto']
export type IntegracaoDoErp = Esquema['IntegracaoDoErp']
export type AcaoAuditada = Esquema['AcaoAuditada']
export type RegistroDeAuditoria = Esquema['RegistroDto']
export type EnderecoInformado = Esquema['EnderecoInformado']
export type EnderecoConsultado = Esquema['EnderecoConsultado']
export type EmpresaConsultada = Esquema['EmpresaConsultada']
export type EstadoDoEncerramento = Esquema['EstadoDoEncerramento']
export type MeuContrato = Esquema['MeuContrato']
export type RegistroDaOperadora = Esquema['RegistroDaOperadoraDto']
export type PaginaDeLotes = Esquema['PaginaDeLotes']
export type ResumoDasVendas = Esquema['ResumoDasVendas']
export type LancamentoDoRazao = Esquema['LancamentoDto']
export type CategoriaDeDespesa = Esquema['CategoriaDeDespesa']
export type NaturezaDaRetirada = Esquema['NaturezaDaRetirada']
export type DreDoMes = Esquema['DreDoMes']
export type MesDoFinanceiro = Esquema['MesDoFinanceiro']
export type MesDoHistorico = Esquema['MesDoHistorico']
export type Despesa = Esquema['DespesaDto']
export type Socio = Esquema['SocioDto']
export type Retirada = Esquema['RetiradaDto']
export type Caixa = Esquema['CaixaDto']

export type ResultadoDoGrupo = Esquema['ResultadoDoGrupoDto']
export type LinhaDoResultado = Esquema['LinhaDoResultado']
export type ReceitaDaRevenda = Esquema['ReceitaDaRevenda']
export type CategoriaDeDespesaDaOperadora = Esquema['CategoriaDeDespesaDaOperadora']
export type DespesaDaOperadora = Esquema['DespesaDaOperadoraDto']
export type CustoDoLote = Esquema['CustoDoLoteDto']

export type Fornecedor = Esquema['FornecedorDto']
export type FuncaoDoFornecedor = 'Grafica' | 'Acrilico' | 'Confeccao' | 'Logistica' | 'Outros'
export type DestinoBloqueado = Esquema['DestinoBloqueadoDto']

export type SaldoAAcertar = Esquema['SaldoAAcertarDto']
export type Acerto = Esquema['AcertoDto']

export type SituacaoDoLimite = Esquema['SituacaoDoLimite']
