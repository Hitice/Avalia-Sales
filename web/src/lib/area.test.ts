import { afterEach, describe, expect, it, vi } from 'vitest'
import { copiar } from './area'

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('copiar', () => {
  it('devolve verdadeiro quando copia', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined)
    vi.stubGlobal('navigator', { clipboard: { writeText } })
    expect(await copiar('abc')).toBe(true)
    expect(writeText).toHaveBeenCalledWith('abc')
  })

  it('permissão negada devolve falso', async () => {
    vi.stubGlobal('navigator', { clipboard: { writeText: () => Promise.reject(new DOMException('negado', 'NotAllowedError')) } })
    expect(await copiar('abc')).toBe(false)
  })

  it('página sem HTTPS, sem navigator.clipboard, devolve falso em vez de lançar', async () => {
    vi.stubGlobal('navigator', {})
    expect(await copiar('abc')).toBe(false)
  })
})
