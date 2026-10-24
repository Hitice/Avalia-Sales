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
export type VendaRegistrada = Esquema['VendaRegistrada']
export type NovaVendaComItens = Esquema['NovaVendaComItens']
/** A resposta do POST /vendas/itens. As vendas vêm na ordem dos itens enviados. */
export type VendasRegistradas = Esquema['VendasRegistradas']
export type LinkCurto = Esquema['LinkDto']
export type Ficha = Esquema['FichaDto']
export type Lugar = Esquema['Lugar']
export type FichaDoCliente = Esquema['FichaDoCliente']
export type DestinoInformado = Esquema['DestinoInformado']
export type Chamado = Esquema['ChamadoDto']
export type ItemDoCatalogo = Esquema['ItemDoCatalogo']
export type ProdutoDaPlataforma = Esquema['ProdutoDaPlataformaDto']
export type TipoDeProduto = Esquema['TipoDeProduto']
export type SituacaoDoProduto = Esquema['SituacaoDoProduto']
export type OrdemDeServico = Esquema['OrdemDto']
export type SituacaoDaOrdem = Esquema['SituacaoDaOrdem']
export type CaixaDeNotificacoes = Esquema['CaixaDeNotificacoes']
export type Notificacao = Esquema['NotificacaoDto']
export type ChamadoComMensagens = Esquema['ChamadoComMensagens']
export type MensagemDoChamado = Esquema['MensagemDto']
export type SituacaoDoChamado = Esquema['SituacaoDoChamado']
export type Cupom = Esquema['CupomDto']
export type SaldoDeComissao = Esquema['SaldoDeComissao']
export type Repasse = Esquema['RepasseDto']
export type ResumoDoMes = Esquema['ResumoDoMes']
export type RenovacaoAberta = Esquema['RenovacaoAberta']
export type EnvioAoErp = Esquema['EnvioDto']
export type AcaoAuditada = Esquema['AcaoAuditada']
export type RegistroDeAuditoria = Esquema['RegistroDto']
export type PaginaDeAuditoria = Esquema['PaginaDeAuditoria']
export type EnderecoInformado = Esquema['EnderecoInformado']
export type EnderecoConsultado = Esquema['EnderecoConsultado']
export type EmpresaConsultada = Esquema['EmpresaConsultada']
export type EstadoDoEncerramento = Esquema['EstadoDoEncerramento']
export type MeuContrato = Esquema['MeuContrato']
export type RegistroDaOperadora = Esquema['RegistroDaOperadoraDto']
export type PaginaDaOperadora = Esquema['PaginaDaOperadora']
export type CategoriaDeDespesa = Esquema['CategoriaDeDespesa']
export type NaturezaDaRetirada = Esquema['NaturezaDaRetirada']
export type DreDoMes = Esquema['DreDoMes']
export type MesDoFinanceiro = Esquema['MesDoFinanceiro']
export type MesDoHistorico = Esquema['MesDoHistorico']
export type Despesa = Esquema['DespesaDto']
export type Socio = Esquema['SocioDto']
export type Retirada = Esquema['RetiradaDto']
