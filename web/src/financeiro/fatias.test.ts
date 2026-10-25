import { describe, expect, it } from 'vitest'
import { fatias, hoje, mesCorrente, mesSomado, percentual } from './fatias'

describe('fatias', () => {
  it('mantém até quatro partes, da maior para a menor', () => {
    expect(fatias([{ nome: 'a', valorCents: 1 }, { nome: 'b', valorCents: 3 }, { nome: 'c', valorCents: 2 }]).map((f) => f.nome)).toEqual(['b', 'c', 'a'])
  })

  it('junta o que passa de quatro em Outros, sem perder centavo', () => {
    const itens = [5, 4, 3, 2, 1].map((v, i) => ({ nome: `p${i}`, valorCents: v * 100 }))
    const resultado = fatias(itens)
    expect(resultado).toHaveLength(4)
    expect(resultado[3]).toEqual({ nome: 'Outros', valorCents: 300 })
    expect(resultado.reduce((s, f) => s + f.valorCents, 0)).toBe(1_500)
  })

  it('deixa de fora parte zerada ou negativa', () => {
    expect(fatias([{ nome: 'a', valorCents: 0 }, { nome: 'b', valorCents: -5 }, { nome: 'c', valorCents: 7 }])).toEqual([{ nome: 'c', valorCents: 7 }])
  })
})

describe('datas em Brasília', () => {
  it('1h de 1º de novembro em UTC ainda é outubro', () => {
    const agora = new Date('2026-11-01T01:00:00Z')
    expect(mesCorrente(agora)).toBe('2026-10')
    expect(hoje(agora)).toBe('2026-10-31')
  })
})

describe('percentual', () => {
  it('arredonda e não divide por zero', () => {
    expect(percentual(1, 3)).toBe('33%')
    expect(percentual(5, 0)).toBe('0%')
  })
})

describe('mesSomado', () => {
  it('passa a virada do ano nos dois sentidos', () => {
    expect(mesSomado('2026-12', 1)).toBe('2027-01')
    expect(mesSomado('2026-01', -2)).toBe('2025-11')
    expect(mesSomado('2026-10', 0)).toBe('2026-10')
  })
})
