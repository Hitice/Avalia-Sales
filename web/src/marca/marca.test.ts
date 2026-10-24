import { describe, expect, it } from 'vitest'
import { BRANCO, PRETO, razao } from './contraste'
import { legivelSobreBranco, variaveisDaMarca } from './marca'

describe('marca da revenda nos tokens', () => {
  it('cor que já passa fica como está', () => {
    expect(legivelSobreBranco('#1D4ED8', 4.5)).toBe('#1D4ED8')
  })

  it('cor clara escurece até dar texto legível (4,5:1) e barra de gráfico visível (3:1)', () => {
    const texto = legivelSobreBranco('#F2B705', 4.5)
    const barra = legivelSobreBranco('#F2B705', 3)
    expect(razao(texto, BRANCO)).toBeGreaterThanOrEqual(4.5)
    expect(razao(barra, BRANCO)).toBeGreaterThanOrEqual(3)
    expect(razao(barra, BRANCO)).toBeLessThan(razao(texto, BRANCO))
  })

  it('a primária vai inteira no botão, com o texto que a API gravou', () => {
    const v = variaveisDaMarca('#F2B705', '#0F5132', '#120C2B')
    expect(v['--primary']).toBe('#F2B705')
    expect(v['--primary-foreground']).toBe('#120C2B')
    expect(v['--ring']).toBe('#0F5132')
    expect(razao(v['--primary-texto'], BRANCO)).toBeGreaterThanOrEqual(4.5)
  })

  it('primária quase preta leva a segunda série do gráfico ao cinza', () => {
    expect(variaveisDaMarca('#120C2B', '#D1006F')['--chart-2']).toBe('var(--cinza-500)')
    expect(variaveisDaMarca('#1D4ED8', '#D1006F')['--chart-2']).toBe('var(--preto)')
    expect(razao('#120C2B', PRETO)).toBeLessThan(2)
  })
})
