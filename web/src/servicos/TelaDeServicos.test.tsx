import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { TelaDeServicosDaOperadora } from './TelaDeServicos'

const ordem = (parte: Record<string, unknown>) => ({
  id: 'o1', numero: 12, revendedor: 'Avalia Vendas', produto: 'Placa personalizada', cliente: 'Padaria', situacao: 'AFazer', observacao: null,
  criadaEm: '2026-10-31T12:00:00Z', iniciadaEm: null, entregueEm: null, quantidade: 1, personalizada: true, loteId: null, ...parte,
})

afterEach(() => vi.unstubAllGlobals())

function abrir(item: Record<string, unknown>) {
  const chamadas: { url: string; metodo: string }[] = []
  vi.stubGlobal('fetch', vi.fn(async (url: string, opcoes?: RequestInit) => {
    const metodo = opcoes?.method ?? 'GET'
    chamadas.push({ url, metodo })
    if (metodo !== 'GET') return new Response(JSON.stringify({ lote: 'LT-0009', codigos: ['K7M2PX', 'Q4R8ZT'] }), { status: 200 })
    return new Response(JSON.stringify({ itens: [item], total: 1, proxima: null }), { status: 200 })
  }))
  render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><MemoryRouter><TelaDeServicosDaOperadora /></MemoryRouter></QueryClientProvider>)
  return chamadas
}

describe('TelaDeServicosDaOperadora', () => {
  it('na placa personalizada sem códigos, a ação é gerar os códigos', async () => {
    const chamadas = abrir(ordem({}))
    fireEvent.click((await screen.findAllByRole('button', { name: 'Gerar códigos' }))[0])

    await waitFor(() => expect(chamadas.some((c) => c.metodo === 'POST')).toBe(true))
    expect(chamadas.find((c) => c.metodo === 'POST')?.url).toBe('/api/operadora/servicos/o1/codigos')
    expect(screen.queryByRole('button', { name: 'Iniciar' })).toBeNull()
  })

  it('com os códigos gerados, entrega e baixa o QR', async () => {
    abrir(ordem({ situacao: 'EmAndamento', loteId: 'l1' }))

    expect((await screen.findAllByRole('button', { name: 'Entregar' })).length).toBeGreaterThan(0)
    expect(screen.queryByRole('button', { name: 'Gerar códigos' })).toBeNull()
  })
})
