import { describe, expect, it } from 'vitest'
import { cnpjValido } from '@/acesso/documentos'
import { formatarBps, formatarData, formatarDataHora, formatarTelefone, paraBps } from './rotulos'

describe('percentual em pontos base', () => {
  it('lê vírgula e ponto sem ponto flutuante', () => {
    expect(paraBps('20')).toBe(2_000)
    expect(paraBps('22,5')).toBe(2_250)
    expect(paraBps('1.99%')).toBe(199)
  })

  it('recusa o que não é percentual', () => {
    expect(paraBps('abc')).toBeNull()
    expect(paraBps('101')).toBeNull()
    expect(paraBps('2,555')).toBeNull()
  })

  it('mostra no formato brasileiro', () => {
    expect(formatarBps(199)).toBe('1,99%')
    expect(formatarBps(2_000)).toBe('20%')
  })
})

describe('cnpjValido', () => {
  it('confere os dígitos como a API', () => {
    expect(cnpjValido('11.222.333/0001-81')).toBe(true)
    expect(cnpjValido('11.222.333/0001-82')).toBe(false)
    expect(cnpjValido('11111111111111')).toBe(false)
  })
})

describe('formatarTelefone', () => {
  it('formata celular, fixo e com o 55', () => {
    expect(formatarTelefone('11977771234')).toBe('(11) 97777-1234')
    expect(formatarTelefone('1133334444')).toBe('(11) 3333-4444')
    expect(formatarTelefone('5511977771234')).toBe('+55 (11) 97777-1234')
  })
})

describe('datas em Brasília', () => {
  it('o dia mostrado é o de Brasília, qualquer que seja o fuso do navegador', () => {
    // 02:59:59 UTC do dia 1º ainda é 31/10 às 23:59:59 em Brasília.
    expect(formatarData('2026-11-01T02:59:59+00:00')).toBe('31/10/2026')
    expect(formatarDataHora('2026-11-01T02:59:59+00:00')).toBe('31/10, 23:59')
    expect(formatarData(null)).toBe('-')
  })
})
