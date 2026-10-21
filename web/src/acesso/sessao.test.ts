import { describe, expect, it } from 'vitest'
import { sessaoValida } from './sessao'

describe('sessaoValida', () => {
  it('vale enquanto o token não expirou', () => {
    expect(sessaoValida({ token: 'x', expiraEm: 2_000 }, 1_000)).toBe(true)
  })

  it('expirada ou sem token, não vale', () => {
    expect(sessaoValida({ token: 'x', expiraEm: 1_000 }, 1_000)).toBe(false)
    expect(sessaoValida({ token: null, expiraEm: null })).toBe(false)
  })
})
