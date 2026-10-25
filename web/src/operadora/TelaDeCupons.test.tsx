import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { useSessao } from '@/acesso/sessao'
import { TelaDeCupons } from './TelaDeCupons'

// O servidor guarda "vale até 31/10" como o fim do dia em Brasília: 23:59:59 -03:00 é 02:59:59 do dia 1º em UTC.
const cupom = { id: 'c1', codigo: 'OUTUBRO', descontoBps: 1000, descontoCents: null, validoAte: '2026-11-01T02:59:59+00:00', usosMaximos: null, usos: 0, ativo: true }

afterEach(() => {
  vi.unstubAllGlobals()
  useSessao.getState().limpar()
})

function abrir() {
  const corpos: unknown[] = []
  vi.stubGlobal('fetch', vi.fn(async (_url: string, opcoes?: RequestInit) => {
    if (opcoes?.method === 'PUT') {
      corpos.push(JSON.parse(String(opcoes.body)))
      return new Response(JSON.stringify(cupom), { status: 200 })
    }
    return new Response(JSON.stringify([cupom]), { status: 200 })
  }))
  render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><TelaDeCupons /></QueryClientProvider>)
  return corpos
}

describe('TelaDeCupons', () => {
  it('editar mostra o mesmo dia que a tabela, e salvar sem mexer na data não a empurra para o dia seguinte', async () => {
    const corpos = abrir()
    await screen.findByText('OUTUBRO')

    fireEvent.click(screen.getByRole('button', { name: /Editar/ }))

    const campo = await screen.findByLabelText('Vale até')
    expect(campo).toHaveValue('2026-10-31')

    fireEvent.click(screen.getByRole('button', { name: 'Salvar cupom' }))
    await waitFor(() => expect(corpos).toHaveLength(1))
    expect(corpos[0]).toMatchObject({ validoAte: '2026-11-01T02:59:59.000Z' })
  })
})
