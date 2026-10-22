import { describe, expect, it } from 'vitest'
import { documentoOpcional, emailOpcional, percentualOpcional, reais, senhaOpcional, telefone } from './esquemas'

const passa = (esquema: { safeParse: (v: unknown) => { success: boolean } }, valor: string) => esquema.safeParse(valor).success

describe('esquemas dos formulários', () => {
  it('opcional vazio passa e preenchido é conferido', () => {
    expect(passa(emailOpcional, '')).toBe(true)
    expect(passa(emailOpcional, 'ana@')).toBe(false)
    expect(passa(documentoOpcional, '')).toBe(true)
    expect(passa(documentoOpcional, '529.982.247-24')).toBe(false)
    expect(passa(percentualOpcional, '')).toBe(true)
    expect(passa(percentualOpcional, '22,5')).toBe(true)
    expect(passa(percentualOpcional, '101')).toBe(false)
  })

  it('dinheiro, telefone e senha seguem a regra da API', () => {
    expect(passa(reais, '149,90')).toBe(true)
    expect(passa(reais, 'abc')).toBe(false)
    expect(passa(telefone, '(34) 99999-0000')).toBe(true)
    expect(passa(senhaOpcional, '')).toBe(true)
    expect(passa(senhaOpcional, 'curta')).toBe(false)
  })
})
