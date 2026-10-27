import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { useSessao } from '@/acesso/sessao'
import { PainelDeAcertos } from './PainelDeAcertos'

const saldo = { revendedorId: 'r1', revendedor: 'Loja Centro', devidoCents: 100_000, acertadoCents: 40_000, saldoCents: 60_000 }
const acerto = { id: 'a1', revendedorId: 'r1', valorCents: 40_000, data: '2026-10-20', observacao: 'Pix', criadoEm: '2026-10-20T15:00:00Z' }

afterEach(() => {
  vi.unstubAllGlobals()
  useSessao.getState().limpar()
})

function abrir() {
  const chamadas: { url: string; metodo: string; corpo: unknown }[] = []
  vi.stubGlobal('fetch', vi.fn(async (url: string, opcoes?: RequestInit) => {
    const metodo = opcoes?.method ?? 'GET'
    chamadas.push({ url, metodo, corpo: opcoes?.body ? JSON.parse(String(opcoes.body)) : undefined })
    if (metodo === 'POST') return new Response(JSON.stringify({}), { status: 201 })
    if (metodo === 'DELETE') return new Response(null, { status: 204 })
    if (url.includes('/acertos')) return new Response(JSON.stringify([acerto]), { status: 200 })
    return new Response(JSON.stringify({ itens: [saldo], total: 1, proxima: null }), { status: 200 })
  }))
  render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><MemoryRouter><PainelDeAcertos /></MemoryRouter></QueryClientProvider>)
  return chamadas
}

describe('PainelDeAcertos', () => {
  it('mostra devido, acertado e saldo, e registra o acerto com valor, data e observação', async () => {
    const chamadas = abrir()
    expect(await screen.findByText('Loja Centro')).toBeInTheDocument()
    expect(screen.getAllByText(/600,00/).length).toBeGreaterThan(0)

    fireEvent.click(screen.getAllByRole('button', { name: 'Registrar acerto' })[0])

    const valor = await screen.findByLabelText('Valor')
    expect(valor).toHaveValue('600,00')
    fireEvent.change(valor, { target: { value: '250,50' } })
    fireEvent.change(screen.getByLabelText('Data'), { target: { value: '2026-10-25' } })
    fireEvent.change(screen.getByLabelText(/Observação/), { target: { value: 'Pix' } })
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Registrar acerto' }))

    await waitFor(() => expect(chamadas.find((c) => c.metodo === 'POST')).toBeDefined())
    expect(chamadas.find((c) => c.metodo === 'POST')).toMatchObject({
      url: '/api/operadora/financeiro/acertos',
      corpo: { revendedorId: 'r1', valorCents: 25_050, data: '2026-10-25', observacao: 'Pix' },
    })
  })

  it('lista os acertos da revenda e exclui um', async () => {
    const chamadas = abrir()
    await screen.findByText('Loja Centro')

    fireEvent.click(screen.getByRole('button', { name: 'Ver acertos' }))

    expect(await screen.findByText(/400,00 em 20\/10\/2026/)).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Excluir' }))

    await waitFor(() => expect(chamadas.find((c) => c.metodo === 'DELETE')).toBeDefined())
    expect(chamadas.find((c) => c.metodo === 'DELETE')?.url).toBe('/api/operadora/financeiro/acertos/a1')
  })
})
