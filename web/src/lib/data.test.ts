import { describe, expect, it } from 'vitest'
import { diaEmBrasilia, fimDoDiaEmBrasilia } from './data'

describe('datas de Brasília', () => {
  it('o fim do dia vai à API em UTC e volta como o mesmo dia', () => {
    expect(fimDoDiaEmBrasilia('2026-10-31')).toBe('2026-11-01T02:59:59.000Z')
    expect(diaEmBrasilia(fimDoDiaEmBrasilia('2026-10-31'))).toBe('2026-10-31')
  })

  it('virada de ano e de mês', () => {
    expect(diaEmBrasilia('2027-01-01T02:59:59Z')).toBe('2026-12-31')
    expect(diaEmBrasilia('2027-01-01T03:00:00Z')).toBe('2027-01-01')
  })
})
