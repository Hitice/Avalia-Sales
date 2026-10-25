import { describe, expect, it } from 'vitest'
import { limparEvento, limparMigalha } from './sentry'

describe('Sentry sem o token da URL', () => {
  it('o evento sai sem o fragmento: /senha#token e /convite#token carregam a credencial de uso único', () => {
    const evento = limparEvento({ request: { url: 'https://sales.exemplo.com.br/senha#Zx9token-secreto' } })
    expect(evento.request?.url).toBe('https://sales.exemplo.com.br/senha')
  })

  it('evento sem URL passa como veio', () => {
    expect(limparEvento({})).toEqual({})
  })

  it('a migalha de navegação perde o fragmento nos dois lados', () => {
    const migalha = limparMigalha({ category: 'navigation', data: { from: '/entrar', to: '/convite#token-secreto' } })
    expect(migalha?.data).toEqual({ from: '/entrar', to: '/convite' })
  })

  it('migalha de outra categoria não muda', () => {
    const migalha = { category: 'ui.click', message: 'button#entrar' }
    expect(limparMigalha(migalha)).toEqual(migalha)
  })
})
