import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { chamarApi } from '@/api/cliente'
import type { ChamadoComMensagens } from '@/api/tipos'
import { ConversaDoChamado } from './Chamados'

vi.mock('@/api/cliente', async (original) => ({ ...(await original<typeof import('@/api/cliente')>()), chamarApi: vi.fn() }))

const api = vi.mocked(chamarApi)

const chamado = {
  id: 'c1', numero: 7, revendedor: 'Revenda', assunto: 'Ajuda', situacao: 'AguardandoAvalia', abertoEm: '2026-10-25T10:00:00Z',
  prazoEm: null, atrasado: false, ultimaMensagemEm: '2026-10-25T12:00:00Z', resolvidoEm: null, naoLidas: 0,
} as const

const mensagem = (n: number) => ({
  id: `m${n}`, daOperadora: n % 2 === 0, autor: 'Alguém', texto: `texto ${n}`, enviadaEm: `2026-10-25T10:${String(n).padStart(2, '0')}:00Z`, lidaEm: null,
})

beforeEach(() => {
  api.mockReset()
  Element.prototype.scrollIntoView = vi.fn()
})

function abrir() {
  const cliente = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(<QueryClientProvider client={cliente}><ConversaDoChamado base="/chamados" id="c1" operadora={false} /></QueryClientProvider>)
}

describe('ConversaDoChamado', () => {
  it('mostra as mensagens mais novas e carrega as anteriores a partir da mais antiga que a tela tem, sem repetir', async () => {
    api.mockImplementation(((caminho: string) => {
      if (caminho.includes('antes=m3')) {
        return Promise.resolve({ chamado, mensagens: [mensagem(1), mensagem(2)], haAnteriores: false } satisfies ChamadoComMensagens)
      }
      return Promise.resolve({ chamado, mensagens: [mensagem(3), mensagem(4), mensagem(5)], haAnteriores: true } satisfies ChamadoComMensagens)
    }) as never)
    abrir()

    const registro = await screen.findByRole('log', { name: 'Mensagens' })
    expect(within(registro).getAllByText(/^texto/).map((p) => p.textContent)).toEqual(['texto 3', 'texto 4', 'texto 5'])

    await userEvent.click(screen.getByRole('button', { name: 'Ver mensagens anteriores' }))
    await waitFor(() => expect(within(registro).getAllByText(/^texto/).map((p) => p.textContent)).toEqual(['texto 1', 'texto 2', 'texto 3', 'texto 4', 'texto 5']))
    expect(api).toHaveBeenCalledWith('/chamados/c1?antes=m3')
    expect(screen.queryByRole('button', { name: 'Ver mensagens anteriores' })).not.toBeInTheDocument()
  })

  it('sem mensagens anteriores, não oferece o botão', async () => {
    api.mockResolvedValue({ chamado, mensagens: [mensagem(1)], haAnteriores: false })
    abrir()
    await screen.findByText('texto 1')
    expect(screen.queryByRole('button', { name: 'Ver mensagens anteriores' })).not.toBeInTheDocument()
  })
})
