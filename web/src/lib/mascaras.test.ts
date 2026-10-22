import { describe, expect, it } from 'vitest'
import { digitos, mascaraCnpj, mascaraCodigo, mascaraDocumento, mascaraTelefone } from './mascaras'

describe('máscaras', () => {
  it('CNPJ aceita só dígitos, até 14, com a pontuação', () => {
    expect(mascaraCnpj('11222333000181')).toBe('11.222.333/0001-81')
    expect(mascaraCnpj('11.222.333/0001-81 extra 99')).toBe('11.222.333/0001-81')
    expect(mascaraCnpj('abc112')).toBe('11.2')
    expect(mascaraCnpj('')).toBe('')
  })

  it('documento é CPF até 11 dígitos e CNPJ depois', () => {
    expect(mascaraDocumento('52998224725')).toBe('529.982.247-25')
    expect(mascaraDocumento('112223330001')).toBe('11.222.333/0001')
  })

  it('telefone fixo e celular', () => {
    expect(mascaraTelefone('3432101234')).toBe('(34) 3210-1234')
    expect(mascaraTelefone('34999990000')).toBe('(34) 99999-0000')
    expect(mascaraTelefone('3')).toBe('(3')
  })

  it('código e dígitos para a API', () => {
    expect(mascaraCodigo('12 34-567')).toBe('123456')
    expect(digitos('(34) 99999-0000')).toBe('34999990000')
  })
})
