import { describe, expect, it } from 'vitest'
import { centavosNoCsv, dataNoCsv, paraCsv } from './exportacao'

describe('exportação em CSV', () => {
  it('separa por ponto e vírgula, protege o que tem aspas, ponto e vírgula e quebra de linha', () => {
    const csv = paraCsv([{ nome: 'Ótica "Bela"; Centro', valor: 99990 }], [
      { titulo: 'Cliente', valor: (l) => l.nome },
      { titulo: 'Valor', valor: (l) => centavosNoCsv(l.valor) },
    ])
    expect(csv).toBe('Cliente;Valor\r\n"Ótica ""Bela""; Centro";999,90')
  })

  it('texto que viraria fórmula na planilha sai como texto; número negativo continua número', () => {
    const csv = paraCsv([{ a: '=HYPERLINK("x")' }, { a: '-24,98' }, { a: '@soma' }], [{ titulo: 'A', valor: (l) => l.a }])
    expect(csv.split('\r\n').slice(1)).toEqual([`"'=HYPERLINK(""x"")"`, '-24,98', "'@soma"])
  })

  it('dinheiro com vírgula e sem milhar; data no dia de Brasília', () => {
    expect(centavosNoCsv(-2498)).toBe('-24,98')
    expect(centavosNoCsv(275771)).toBe('2757,71')
    expect(dataNoCsv('2026-10-31')).toBe('31/10/2026')
    expect(dataNoCsv('2026-11-01T02:30:00Z')).toBe('31/10/2026')
    expect(dataNoCsv(null)).toBe('')
  })
})
