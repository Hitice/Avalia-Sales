import { describe, expect, it } from 'vitest'
import type { ItemDoCatalogo } from '@/api/tipos'
import { ITENS_MAXIMOS, adicionar, comQuantidade, comValor, itensParaVender, linhaDePlaca, linhaDeServico, nomeDaLinha, paraItens, remover, soPlacas, total } from './itens'

const item = (parte: Partial<ItemDoCatalogo>): ItemDoCatalogo => ({
  id: parte.id ?? 'p1', chave: 'placa', nome: 'Placa de avaliação', descricao: '', tipo: 'Placa', situacao: 'Disponivel', fotoUrl: null,
  precoSugeridoCents: 14990, precoCents: 14990, comissaoBps: null, pisoCents: 1, ativo: true, quantidadeMinima: 1, ...parte,
})
const placa = item({})
const kitMesa = item({ id: 's1', chave: 'kit-mesa', nome: 'Kit Mesa', tipo: 'Servico', precoCents: 1990, quantidadeMinima: 10 })
const destino = { tipo: 'Link' as const, lugarNoGoogle: null, link: 'cardapio.com.br' }

describe('itens da venda', () => {
  it('serviço entra com o pedido mínimo e nunca desce abaixo dele', () => {
    const linhas = adicionar([], linhaDeServico(kitMesa))
    expect(linhas[0].quantidade).toBe(10)
    expect(comQuantidade(linhas, linhas[0].id, 3)[0].quantidade).toBe(10)
    expect(comQuantidade(linhas, linhas[0].id, 12)[0].quantidade).toBe(12)
    expect(comQuantidade(linhas, linhas[0].id, Number.NaN)[0].quantidade).toBe(10)
  })

  it('o mesmo serviço de novo soma uma unidade; placa é sempre linha nova', () => {
    let linhas = adicionar([], linhaDeServico(kitMesa))
    linhas = adicionar(linhas, linhaDeServico(kitMesa))
    expect(linhas).toHaveLength(1)
    expect(linhas[0].quantidade).toBe(11)
    linhas = adicionar(linhas, linhaDePlaca(placa, 'K7M2PX', destino, null))
    linhas = adicionar(linhas, linhaDePlaca(placa, 'ABC234', destino, null))
    expect(linhas).toHaveLength(3)
  })

  it('não passa do limite de itens da API', () => {
    let linhas = adicionar([], linhaDeServico(kitMesa))
    for (let i = 0; i < ITENS_MAXIMOS + 3; i++) linhas = adicionar(linhas, linhaDePlaca(placa, `C${String(i).padStart(5, '0')}`, destino, null))
    expect(linhas).toHaveLength(ITENS_MAXIMOS)
  })

  it('soma o total pela tabela, ou pelo valor ajustado da linha', () => {
    let linhas = adicionar([linhaDePlaca(placa, 'K7M2PX', destino, null)], linhaDeServico(kitMesa))
    expect(total(linhas)).toBe(14990 + 10 * 1990)
    linhas = comValor(linhas, linhas[0].id, 10000)
    expect(total(linhas)).toBe(10000 + 19900)
    // Mudar a quantidade apaga o ajuste: ele era do total de antes.
    linhas = comValor(linhas, linhas[1].id, 15000)
    expect(comQuantidade(linhas, linhas[1].id, 11)[1].valorCents).toBeNull()
  })

  it('teste gratuito só quando tudo é placa', () => {
    const soPlaca = [linhaDePlaca(placa, 'K7M2PX', destino, null)]
    expect(soPlacas(soPlaca)).toBe(true)
    expect(soPlacas([...soPlaca, linhaDeServico(kitMesa)])).toBe(false)
    expect(soPlacas([])).toBe(false)
  })

  it('monta os itens da API e o nome de cada linha', () => {
    const linhas = [linhaDePlaca(placa, 'K7M2PX', destino, null), linhaDeServico(kitMesa)]
    expect(paraItens(linhas)).toEqual([
      { produtoId: 'p1', codigo: 'K7M2PX', destino, valorCents: null, quantidade: 1 },
      { produtoId: 's1', codigo: null, destino: null, valorCents: null, quantidade: 10 },
    ])
    expect(nomeDaLinha(linhas[0])).toBe('Placa de avaliação, K7M2PX')
    expect(nomeDaLinha(linhas[1])).toBe('10x Kit Mesa')
    expect(remover(linhas, linhas[0].id)).toHaveLength(1)
  })

  it('para vender: só o disponível e ativo, placas primeiro', () => {
    const lista = itensParaVender([kitMesa, item({ id: 'x', ativo: false }), item({ id: 'y', situacao: 'Esgotado' }), placa])
    expect(lista.map((p) => p.id)).toEqual(['p1', 's1'])
  })
})
