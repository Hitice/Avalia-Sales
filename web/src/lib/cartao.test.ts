import { describe, expect, it } from 'vitest'
import { cartaoValido, tokenizarCartao, validadeValida } from './cartao'

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
    const config = { disponivel: true, chavePublica: null, simulado: true, parcelasMaximas: 12 }
    const dados = { numero: '4000000000000002', nome: 'Ana', validade: '12/30', cvv: '123' }
    expect(await tokenizarCartao(config, dados)).toMatch(/^token_simulado_recusado_/)
  })
})
