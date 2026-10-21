import { describe, expect, it } from 'vitest'
import { extrairCodigo } from './codigo'

describe('extrairCodigo', () => {
  it('tira o código da URL do QR, em maiúsculas ou minúsculas', () => {
    expect(extrairCodigo('HTTPS://AVALIAONE.COM.BR/Q/K7M2PX')).toBe('K7M2PX')
    expect(extrairCodigo('https://placas.exemplo.com.br/q/k7m2px?x=1')).toBe('K7M2PX')
  })

  it('normaliza o digitado como a API: I e L viram 1, O vira 0', () => {
    expect(extrairCodigo(' k7m-2px ')).toBe('K7M2PX')
    expect(extrairCodigo('I0LOAB')).toBe('1010AB')
  })

  it('recusa o que não é código de placa', () => {
    expect(extrairCodigo('K7M2PU')).toBe('')
    expect(extrairCodigo('https://outro.site/qualquer')).toBe('')
    expect(extrairCodigo('K7M2P')).toBe('')
  })
})
