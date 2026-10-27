import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { useSessao } from '@/acesso/sessao'
import type { Perfil } from '@/acesso/sessao'
import { AvisoDoLimiteDeDestinos } from './AvisoDoLimiteDeDestinos'

afterEach(() => {
  vi.unstubAllGlobals()
  useSessao.setState({ perfil: null })
})

function abrir(papel: 'Dono' | 'Vendedor', situacao: { usados: number; limite: number; esgotado: boolean }) {
  useSessao.setState({ perfil: { papel } as Perfil })
  const envios: unknown[] = []
  vi.stubGlobal('fetch', vi.fn(async (_url: string, opcoes?: RequestInit) => {
    if (opcoes?.method === 'POST') {
      envios.push(JSON.parse(String(opcoes.body)))
      return new Response(null, { status: 204 })
    }
    return new Response(JSON.stringify(situacao), { status: 200 })
  }))
  const cliente = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(<QueryClientProvider client={cliente}><AvisoDoLimiteDeDestinos /></QueryClientProvider>)
  return { envios, cliente }
}

describe('AvisoDoLimiteDeDestinos', () => {
  it('esgotado, o dono pede a liberação com o motivo e vê o pedido enviado', async () => {
    const { envios } = abrir('Dono', { usados: 10, limite: 10, esgotado: true })
    expect(await screen.findByText('Hoje: 10 de 10 cadastros de destino')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Solicitar liberação' }))
    const dialogo = await screen.findByRole('dialog')
    fireEvent.change(within(dialogo).getByLabelText('Motivo (opcional)'), { target: { value: 'Campanha de hoje' } })
    fireEvent.click(within(dialogo).getByRole('button', { name: 'Confirmar' }))

    await waitFor(() => expect(envios).toEqual([{ motivo: 'Campanha de hoje' }]))
    expect(await screen.findByText('Pedido enviado')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Solicitar liberação' })).toBeNull()
  })

  it('esgotado, o vendedor é mandado ao revendedor e não vê o botão', async () => {
    abrir('Vendedor', { usados: 10, limite: 10, esgotado: true })
    expect(await screen.findByText(/Peça ao revendedor para solicitar a liberação/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Solicitar liberação' })).toBeNull()
  })

  it('perto do limite mostra só a contagem', async () => {
    abrir('Dono', { usados: 8, limite: 10, esgotado: false })
    expect(await screen.findByText('Hoje: 8 de 10 cadastros de destino')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Solicitar liberação' })).toBeNull()
  })

  it('com folga, não aparece', async () => {
    const { cliente } = abrir('Dono', { usados: 2, limite: 10, esgotado: false })
    // A consulta tem de ter chegado: antes dela a tela também fica vazia.
    await waitFor(() => expect(cliente.getQueryCache().getAll().map((q) => q.state.status)).toEqual(['success']))
    expect(screen.queryByText(/cadastros de destino/)).toBeNull()
  })
})
