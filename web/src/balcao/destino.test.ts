import { describe, expect, it } from 'vitest'
import { cpfValido, documentoValido, emailValido, telefoneValido } from '@/acesso/documentos'
import { destinoPronto, linkDoGoogleValido } from './destino'

describe('destino da venda', () => {
  it('aceita o link do perfil com ou sem https e recusa outro domínio', () => {
    expect(linkDoGoogleValido('g.page/r/CbX123/review')).toBe(true)
    expect(linkDoGoogleValido('https://search.google.com/local/writereview?placeid=ChIJ0123456789')).toBe(true)
    expect(linkDoGoogleValido('https://instagram.com/padaria')).toBe(false)
    expect(linkDoGoogleValido('nao e link')).toBe(false)
  })

  it('segue com lugar escolhido ou link aceito, e não com link recusado', () => {
    expect(destinoPronto({ tipo: 'Google', lugarNoGoogle: 'ChIJ0123456789', link: null })).toBe(true)
    expect(destinoPronto({ tipo: 'Google', lugarNoGoogle: null, link: 'instagram.com/padaria' })).toBe(false)
    expect(destinoPronto({ tipo: 'Link', lugarNoGoogle: null, link: 'cardapio.exemplo.com.br' })).toBe(true)
  })
})

describe('campos do cliente', () => {
  it('confere o dígito do CPF e aceita CPF ou CNPJ no mesmo campo', () => {
    expect(cpfValido('529.982.247-25')).toBe(true)
    expect(cpfValido('529.982.247-24')).toBe(false)
    expect(cpfValido('111.111.111-11')).toBe(false)
    expect(documentoValido('11.222.333/0001-81')).toBe(true)
  })

  it('confere a forma do e-mail e do telefone', () => {
    expect(emailValido('marta@padaria.com.br')).toBe(true)
    expect(emailValido('marta@padaria')).toBe(false)
    expect(telefoneValido('(34) 99999-0000')).toBe(true)
    expect(telefoneValido('9999-0000')).toBe(false)
  })
})
