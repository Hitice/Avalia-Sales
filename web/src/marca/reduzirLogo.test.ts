import { describe, expect, it } from 'vitest'
import { medidas } from './reduzirLogo'

describe('medidas do logo', () => {
  it('reduz a foto do celular para 512 px no lado maior, sem distorcer', () => {
    expect(medidas(4032, 3024)).toEqual({ largura: 512, altura: 384 })
    expect(medidas(1080, 1920)).toEqual({ largura: 288, altura: 512 })
  })

  it('não aumenta imagem pequena', () => {
    expect(medidas(200, 80)).toEqual({ largura: 200, altura: 80 })
  })
})
