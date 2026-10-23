import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ehFalhaDeCarregamento, recarregarParaVersaoNova } from './recarga'

describe('recarga depois de publicação nova', () => {
  const recarregar = vi.fn()

  beforeEach(() => {
    sessionStorage.clear()
    vi.stubGlobal('location', { ...window.location, reload: recarregar })
  })

  afterEach(() => {
    recarregar.mockReset()
    vi.unstubAllGlobals()
  })

  it('reconhece o arquivo da tela que sumiu, no Safari, no Chrome e no Firefox', () => {
    expect(ehFalhaDeCarregamento(new TypeError('Importing a module script failed.'))).toBe(true)
    expect(ehFalhaDeCarregamento(new TypeError('Failed to fetch dynamically imported module: https://x/assets/a.js'))).toBe(true)
    expect(ehFalhaDeCarregamento(new TypeError('error loading dynamically imported module'))).toBe(true)
    expect(ehFalhaDeCarregamento(new Error('Cannot read properties of undefined'))).toBe(false)
    expect(ehFalhaDeCarregamento('texto')).toBe(false)
  })

  it('recarrega uma vez e não entra em laço se falhar de novo no mesmo minuto', () => {
    expect(recarregarParaVersaoNova()).toBe(true)
    expect(recarregarParaVersaoNova()).toBe(false)
    expect(recarregar).toHaveBeenCalledTimes(1)
  })
})
