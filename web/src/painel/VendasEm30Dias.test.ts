import { afterEach, describe, expect, it } from 'vitest'
import { porDiaDe } from './VendasEm30Dias'

const fusoOriginal = process.env.TZ

afterEach(() => {
  process.env.TZ = fusoOriginal
})

describe('30 dias de Brasília', () => {
  it.each(['Asia/Tokyo', 'Europe/Lisbon', 'America/Manaus'])('com o navegador em %s, os dias e a contagem seguem Brasília', (fuso) => {
    process.env.TZ = fuso
    // 27/10/2026 às 23h30 em Brasília: já é 28/10 em UTC, em Lisboa e em Tóquio.
    const agora = Date.parse('2026-10-27T23:30:00-03:00')

    const dias = porDiaDe([{ dia: '2026-10-27', total: 4 }, { dia: '2026-09-28', total: 1 }], agora)

    expect(dias).toHaveLength(30)
    expect(dias[29]).toEqual({ dia: '27/10', total: 4 })
    expect(dias[0]).toEqual({ dia: '28/09', total: 1 })
  })
})
