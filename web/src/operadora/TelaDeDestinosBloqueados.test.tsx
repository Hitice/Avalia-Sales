import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { useSessao } from '@/acesso/sessao'
import { TelaDeDestinosBloqueados } from './TelaDeDestinosBloqueados'

const destino = { id: 'd1', dominio: 'golpe.exemplo.com', motivo: 'Phishing', criadoEm: '2026-10-20T15:00:00+00:00' }

afterEach(() => {
  vi.unstubAllGlobals()
  useSessao.getState().limpar()
})

function abrir() {
  const envios: { metodo: string; url: string; corpo: unknown }[] = []
  vi.stubGlobal('fetch', vi.fn(async (url: string, opcoes?: RequestInit) => {
    if (opcoes?.method && opcoes.method !== 'GET') {
      envios.push({ metodo: opcoes.method, url: String(url), corpo: opcoes.body ? JSON.parse(String(opcoes.body)) : null })
      return new Response(opcoes.method === 'DELETE' ? null : JSON.stringify(destino), { status: opcoes.method === 'DELETE' ? 204 : 201 })
    }
    return new Response(JSON.stringify({ itens: [destino], total: 1, proxima: null }), { status: 200 })
  }))
  render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><TelaDeDestinosBloqueados /></QueryClientProvider>)
  return envios
}

describe('TelaDeDestinosBloqueados', () => {
  it('lista o domínio com o motivo', async () => {
    abrir()
    expect(await screen.findByText('golpe.exemplo.com')).toBeInTheDocument()
    expect(screen.getByText('Phishing')).toBeInTheDocument()
  })

  it('bloqueia com o endereço digitado e o motivo', async () => {
    const envios = abrir()
    await screen.findByText('golpe.exemplo.com')

    fireEvent.click(screen.getByRole('button', { name: 'Bloquear domínio' }))
    expect(await screen.findByText('Digite o domínio ou o endereço.')).toBeInTheDocument()
    expect(envios).toHaveLength(0)

    fireEvent.change(screen.getByLabelText('Domínio ou endereço'), { target: { value: 'https://novo.exemplo.com/x' } })
    fireEvent.change(screen.getByLabelText('Motivo'), { target: { value: 'Fraude' } })
    fireEvent.click(screen.getByRole('button', { name: 'Bloquear domínio' }))
    await waitFor(() => expect(envios).toHaveLength(1))
    expect(envios[0]).toMatchObject({ metodo: 'POST', corpo: { dominio: 'https://novo.exemplo.com/x', motivo: 'Fraude' } })
  })

  it('desbloqueia pela linha', async () => {
    const envios = abrir()
    await screen.findByText('golpe.exemplo.com')
    fireEvent.click(screen.getAllByRole('button', { name: 'Desbloquear' })[0])
    await waitFor(() => expect(envios).toHaveLength(1))
    expect(envios[0]).toMatchObject({ metodo: 'DELETE', url: expect.stringContaining('/operadora/destinos-bloqueados/d1') })
  })
})
