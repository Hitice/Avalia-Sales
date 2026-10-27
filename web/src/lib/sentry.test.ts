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

  it('a busca de cliente não sai na migalha de fetch, de XHR nem de navegação', () => {
    expect(limparMigalha({ category: 'fetch', data: { method: 'GET', url: '/api/clientes?busca=Padaria%20da%20Marta&pagina=1', status_code: 200 } })?.data)
      .toEqual({ method: 'GET', url: '/api/clientes', status_code: 200 })
    expect(limparMigalha({ category: 'xhr', data: { url: '/api/vendas?busca=Marta' } })?.data).toEqual({ url: '/api/vendas' })
    expect(limparMigalha({ category: 'navigation', data: { from: '/painel/clientes?busca=Marta', to: '/painel' } })?.data).toEqual({ from: '/painel/clientes', to: '/painel' })
  })

  it('o evento sai sem a consulta da URL', () => {
    const evento = limparEvento({ request: { url: 'https://sales.exemplo.com.br/painel/clientes?busca=Marta', query_string: 'busca=Marta' } })
    expect(evento.request).toEqual({ url: 'https://sales.exemplo.com.br/painel/clientes' })
  })

  it('migalha de outra categoria não muda', () => {
    const migalha = { category: 'ui.click', message: 'button#entrar' }
    expect(limparMigalha(migalha)).toEqual(migalha)
  })
})
