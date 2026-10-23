import { describe, expect, it } from 'vitest'
import { cartaoValido, tokenizarCartao, totalParcelado, validadeValida } from './cartao'

describe('cartão', () => {
  it('confere o dígito do cartão', () => {
    expect(cartaoValido('4000 0000 0000 0010')).toBe(true)
    expect(cartaoValido('4000 0000 0000 0011')).toBe(false)
    expect(cartaoValido('1234')).toBe(false)
  })

  it('recusa validade vencida ou fora do formato', () => {
    const outubro2026 = new Date(2026, 9, 22)
    expect(validadeValida('10/26', outubro2026)).toBe(true)
    expect(validadeValida('09/26', outubro2026)).toBe(false)
    expect(validadeValida('13/30', outubro2026)).toBe(false)
  })

  it('no simulador não chama o Pagar.me e o final 0002 vira recusa', async () => {
    const config = { disponivel: true, chavePublica: null, simulado: true, parcelasMaximas: 12, custoDasParcelasBps: [] }
    const dados = { numero: '4000000000000002', nome: 'Ana', validade: '12/30', cvv: '123' }
    expect(await tokenizarCartao(config, dados)).toMatch(/^token_simulado_recusado_/)
  })
})

describe('totalParcelado', () => {
  const custo = [511, 817, 947, 1_077, 1_207, 1_338, 1_566, 1_695, 1_823, 1_952, 2_081, 2_209]

  it('à vista é o valor; parcelado soma o custo, igual à API', () => {
    expect(totalParcelado(89_900, 1, custo)).toBe(89_900)
    expect(totalParcelado(89_900, 2, custo)).toBe(97_899)
    expect(totalParcelado(89_900, 12, custo)).toBe(115_390)
  })
})
