import { describe, expect, it } from 'vitest'
import { formatarCentavos, paraCentavos } from './dinheiro'

describe('paraCentavos', () => {
  it.each([
    ['149,90', 14990],
    ['1.234,56', 123456],
    ['149.90', 14990],
    ['149', 14900],
    ['R$ 9,9', 990],
    ['0,01', 1],
    ['1.234', 123400],
  ])('lê "%s" como %i centavos', (texto, centavos) => {
    expect(paraCentavos(texto)).toBe(centavos)
  })

  it.each(['', 'abc', '-10', '1,2,3', '10,999', '1.2.3,4.5'])('recusa "%s"', (texto) => {
    expect(paraCentavos(texto)).toBeNull()
  })

  it('não erra o centavo onde o ponto flutuante erraria', () => {
    expect(paraCentavos('0,29')).toBe(29)
    expect(paraCentavos('1,15')).toBe(115)
  })
})

describe('formatarCentavos', () => {
  it('mostra em reais', () => {
    expect(formatarCentavos(14990).replace(/\s/g, ' ')).toBe('R$ 149,90')
  })
})
