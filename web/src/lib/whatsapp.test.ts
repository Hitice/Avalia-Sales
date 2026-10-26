import { describe, expect, it } from 'vitest'
import { linkDoWhatsapp, numeroDoWhatsapp } from './whatsapp'

describe('whatsapp', () => {
  it('acrescenta o 55 ao telefone com DDD e mantém o que já tem código de país', () => {
    expect(numeroDoWhatsapp('(11) 98888-7777')).toBe('5511988887777')
    expect(numeroDoWhatsapp('5511988887777')).toBe('5511988887777')
  })

  it('monta o link com a mensagem codificada', () => {
    expect(linkDoWhatsapp('11988887777', 'Olá, tudo bem?')).toBe('https://wa.me/5511988887777?text=Ol%C3%A1%2C%20tudo%20bem%3F')
  })
})
