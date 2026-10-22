import type { components } from './esquema'

/**
 * Os formatos da API, gerados do contrato (web/openapi.json, `npm run tipos`)
 * e apelidados com os nomes que as telas usam. Tipo escrito à mão aqui só
 * para o que o contrato não descreve. Dinheiro sempre em centavos.
 */
type Esquema = components['schemas']

export type EstadoDaPlaca = Esquema['EstadoDaPlaca']
export type SituacaoDoPedido = Esquema['SituacaoDoPedido']
// O gerador do .NET põe null na enumeração quando algum DTO a usa como opcional; o nulo fica no campo.
export type SituacaoDaCobranca = NonNullable<Esquema['SituacaoDaCobranca']>
export type SituacaoDoContrato = NonNullable<Esquema['SituacaoDoContrato']>
export type MeioDePagamento = Esquema['MeioDePagamento']
export type TipoDeDestino = Esquema['TipoDeDestino']
export type TipoDeLancamento = Esquema['TipoDeLancamento']

export type Pacote = Esquema['PacoteDto']
export type Pedido = Esquema['PedidoDto']
export type Lote = Esquema['LoteDto']
export type Revendedor = Esquema['RevendedorDto']
export type MinhaRevenda = Esquema['MinhaRevenda']
export type PlacaDaLista = Esquema['PlacaDaLista']
export type PaginaDePlacas = Omit<Esquema['PaginaDePlacas'], 'porEstado'> & { porEstado: Partial<Record<EstadoDaPlaca, number>> }
export type Vendedor = Esquema['VendedorDto']
export type VendaDaLista = Esquema['VendaDaLista']
export type Cobranca = Esquema['CobrancaDto']
export type VendaRegistrada = Esquema['VendaRegistrada']
export type LinkCurto = Esquema['LinkDto']
export type Ficha = Esquema['FichaDto']
export type Lugar = Esquema['Lugar']
export type DestinoInformado = Esquema['DestinoInformado']
export type Cupom = Esquema['CupomDto']
export type SaldoDeComissao = Esquema['SaldoDeComissao']
export type Repasse = Esquema['RepasseDto']
export type ResumoDoMes = Esquema['ResumoDoMes']
export type RenovacaoAberta = Esquema['RenovacaoAberta']
export type EnvioAoErp = Esquema['EnvioDto']
export type AcaoAuditada = Esquema['AcaoAuditada']
export type RegistroDeAuditoria = Esquema['RegistroDto']
export type PaginaDeAuditoria = Esquema['PaginaDeAuditoria']
