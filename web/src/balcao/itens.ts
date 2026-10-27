import type { components } from '@/api/esquema'
import type { DestinoInformado, ItemDoCatalogo, Lugar } from '@/api/tipos'
import { usaPlaca } from '@/catalogo/produtos'

export type ItemDaVenda = components['schemas']['ItemDaVenda']

/**
 * Uma linha da venda no balcão: a placa lida (uma por código, com o destino)
 * ou o serviço com a quantidade. O valor só fica preenchido quando o vendedor
 * cobrou diferente da tabela; é o total da linha, como a API espera.
 */
export type LinhaDaVenda = {
  id: string
  item: ItemDoCatalogo
  quantidade: number
  codigo: string | null
  destino: DestinoInformado | null
  lugar: Lugar | null
  valorCents: number | null
}

/** Os mesmos limites da API: Vendas.ItensMaximos e Venda.QuantidadeMaxima. */
export const ITENS_MAXIMOS = 20
export const QUANTIDADE_MAXIMA = 1_000

export const quantidadeMinima = (item: ItemDoCatalogo) => Math.max(1, item.quantidadeMinima ?? 1)

// O randomUUID só existe em página segura (HTTPS ou localhost); o getRandomValues
// existe também no Vite aberto pelo IP da rede, no celular.
const novoId = () => (typeof crypto.randomUUID === 'function'
  ? crypto.randomUUID()
  : Array.from(crypto.getRandomValues(new Uint32Array(4)), (n) => n.toString(16).padStart(8, '0')).join(''))

export function linhaDePlaca(item: ItemDoCatalogo, codigo: string, destino: DestinoInformado, lugar: Lugar | null): LinhaDaVenda {
  return { id: novoId(), item, quantidade: 1, codigo, destino, lugar, valorCents: null }
}

export function linhaDeServico(item: ItemDoCatalogo): LinhaDaVenda {
  return { id: novoId(), item, quantidade: quantidadeMinima(item), codigo: null, destino: null, lugar: null, valorCents: null }
}

/**
 * Serviço já na venda ganha mais uma unidade em vez de outra linha; placa é
 * sempre linha nova, uma por código. No limite, a venda não muda.
 */
export function adicionar(linhas: LinhaDaVenda[], nova: LinhaDaVenda): LinhaDaVenda[] {
  if (!usaPlaca(nova.item)) {
    const existente = linhas.find((l) => l.item.id === nova.item.id)
    if (existente) return comQuantidade(linhas, existente.id, existente.quantidade + 1)
  }
  if (linhas.length >= ITENS_MAXIMOS) return linhas
  return [...linhas, nova]
}

export const remover = (linhas: LinhaDaVenda[], id: string) => linhas.filter((l) => l.id !== id)

/** Nunca abaixo do pedido mínimo do item nem acima do limite da API; mudar a quantidade apaga o valor ajustado. */
export function comQuantidade(linhas: LinhaDaVenda[], id: string, quantidade: number): LinhaDaVenda[] {
  return linhas.map((l) => {
    if (l.id !== id) return l
    const limitada = Math.min(QUANTIDADE_MAXIMA, Math.max(quantidadeMinima(l.item), Math.trunc(quantidade) || 0))
    return limitada === l.quantidade ? l : { ...l, quantidade: limitada, valorCents: null }
  })
}

export const comValor = (linhas: LinhaDaVenda[], id: string, valorCents: number | null) =>
  linhas.map((l) => (l.id === id ? { ...l, valorCents } : l))

export const totalDaLinha = (l: LinhaDaVenda) => l.valorCents ?? l.item.precoCents * l.quantidade
export const total = (linhas: LinhaDaVenda[]) => linhas.reduce((soma, l) => soma + totalDaLinha(l), 0)

/** Teste gratuito é de uma placa de avaliação no Google só: com outro item ou outro destino, a opção some. */
export const cabeTeste = (linhas: LinhaDaVenda[]) => linhas.length === 1 && usaPlaca(linhas[0].item) && linhas[0].destino?.tipo === 'Google'

export const codigosNaVenda = (linhas: LinhaDaVenda[]) => linhas.map((l) => l.codigo).filter((c): c is string => !!c)

/** Como vai na NovaVendaComItens: placa com código e destino, serviço com a quantidade. */
export const paraItens = (linhas: LinhaDaVenda[]): ItemDaVenda[] =>
  linhas.map((l) => ({ produtoId: l.item.id, codigo: l.codigo, destino: l.destino, valorCents: l.valorCents, quantidade: l.quantidade }))

/** O nome da linha na tela: "10x Kit Mesa" ou "Placa K7M2PX". */
export const nomeDaLinha = (l: LinhaDaVenda) => (l.codigo ? `${l.item.nome}, ${l.codigo}` : l.quantidade > 1 ? `${l.quantidade}x ${l.item.nome}` : l.item.nome)

/** Primeiro quem leva placa, na ordem do catálogo; só o que está à venda. */
export function itensParaVender(catalogo: ItemDoCatalogo[]): ItemDoCatalogo[] {
  const ativos = catalogo.filter((p) => p.ativo && p.situacao === 'Disponivel')
  return [...ativos.filter(usaPlaca), ...ativos.filter((p) => !usaPlaca(p))]
}
