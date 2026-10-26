import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { useSessao } from '@/acesso/sessao'
import { TelaDeSeguranca } from './TelaDeSeguranca'

afterEach(() => {
  vi.unstubAllGlobals()
  useSessao.setState({ perfil: null })
})

function abrir(papel: 'Dono' | 'Vendedor') {
  useSessao.setState({ perfil: { id: 'u1', papel, nome: 'Ju', email: 'ju@x.com', emailConfirmado: true, revendedor: 'Loja' } as never })
  vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ ligado: false, emailConfirmado: true }), { status: 200 })))
  render(<MemoryRouter><QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><TelaDeSeguranca /></QueryClientProvider></MemoryRouter>)
}

describe('TelaDeSeguranca', () => {
  it('o vendedor troca a senha e não vê o segundo fator', () => {
    abrir('Vendedor')
    expect(screen.getByRole('button', { name: 'Trocar senha' })).toBeInTheDocument()
    expect(screen.queryByText('Autenticação de dois fatores')).toBeNull()
  })

  it('o dono vê a senha e o segundo fator', async () => {
    abrir('Dono')
    expect(screen.getByRole('button', { name: 'Trocar senha' })).toBeInTheDocument()
    expect(await screen.findByText('Autenticação de dois fatores')).toBeInTheDocument()
  })
})
