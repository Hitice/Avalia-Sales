import { describe, expect, it } from 'vitest'
import { problemasDaMarca, razao, textoSobre } from './contraste'

describe('contraste', () => {
  it('faz a mesma conta da API', () => {
    expect(razao('#FFFFFF', '#000000')).toBeCloseTo(21, 1)
    expect(razao('#777777', '#FFFFFF')).toBeCloseTo(4.48, 2)
    expect(textoSobre('#FFD400')).toBe('#120C2B')
  })

  it('aponta o que a API recusaria', () => {
    expect(problemasDaMarca('#0B3D91', '#D1006F')).toEqual([])
    expect(problemasDaMarca('#0B3D91', '#FAFAFA')).toEqual(['A cor de destaque some no fundo branco.'])
    expect(problemasDaMarca('azul', '#D1006F')).toHaveLength(1)
  })
})
