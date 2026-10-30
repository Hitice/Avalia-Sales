import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { useSessao } from '@/acesso/sessao'
import { TelaDePedidos } from './TelaDePedidos'

const pedido = {
  id: 'p1', revendedor: 'Loja Centro', quantidade: 50, valorCents: 100_000, criadoEm: '2026-10-20T15:00:00Z', situacao: 'Pago',
  lote: null, haLoteLivre: true, retirada: false, transportadora: null, codigoDeRastreio: null,
}
const lote = { id: 'l1', numero: 7, rotulo: 'Lote 7' }

beforeAll(() => {
  // O Radix Select rola até a opção; o jsdom não tem o método.
  Element.prototype.scrollIntoView = vi.fn()
  Element.prototype.hasPointerCapture = vi.fn(() => false)
})

afterEach(() => {
  vi.unstubAllGlobals()
  useSessao.getState().limpar()
})

function abrir() {
  const chamadas: { url: string; metodo: string; corpo: Record<string, unknown> | undefined }[] = []
  vi.stubGlobal('fetch', vi.fn(async (url: string, opcoes?: RequestInit) => {
    const metodo = opcoes?.method ?? 'GET'
    chamadas.push({ url, metodo, corpo: opcoes?.body ? JSON.parse(String(opcoes.body)) : undefined })
    if (metodo !== 'GET') return new Response(null, { status: 204 })
    if (url.includes('/operadora/fornecedores')) return new Response(JSON.stringify({ itens: [{ id: 'f1', nome: 'Correios' }], total: 1, proxima: null }), { status: 200 })
    if (url.includes('/operadora/revendedores')) {
      return new Response(JSON.stringify({
        itens: [
          { id: 'r1', nome: 'Avalia Vendas', contratoAssinadoEm: '2026-10-01T00:00:00Z', encerradaEm: null, suspensaEm: null },
          { id: 'r2', nome: 'Loja Sem Contrato', contratoAssinadoEm: null, encerradaEm: null, suspensaEm: null },
        ],
        total: 2,
        proxima: null,
      }), { status: 200 })
    }
    if (url.includes('/operadora/lotes')) return new Response(JSON.stringify({ itens: [lote], total: 1, proxima: null }), { status: 200 })
    return new Response(JSON.stringify({ itens: [pedido], total: 1, proxima: null }), { status: 200 })
  }))
  render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><MemoryRouter><TelaDePedidos /></MemoryRouter></QueryClientProvider>)
  return chamadas
}

describe('TelaDePedidos', () => {
  it('abre pedido avulso fora dos pacotes, só para revenda com contrato', async () => {
    const chamadas = abrir()
    fireEvent.click(await screen.findByRole('button', { name: 'Novo pedido' }))
    fireEvent.click(await screen.findByLabelText('Revenda'))
    expect(screen.queryByRole('option', { name: 'Loja Sem Contrato' })).toBeNull()
    fireEvent.click(await screen.findByRole('option', { name: 'Avalia Vendas' }))
    fireEvent.change(screen.getByLabelText('Placas'), { target: { value: '2' } })
    fireEvent.click(screen.getByRole('button', { name: 'Abrir pedido' }))

    await waitFor(() => expect(chamadas.find((c) => c.metodo === 'POST')).toBeDefined())
    expect(chamadas.find((c) => c.metodo === 'POST')).toMatchObject({
      url: '/api/operadora/pedidos',
      corpo: { revendedorId: 'r1', quantidade: 2, valorCents: 0 },
    })
  })

  it('despacha com transportadora e código de rastreio', async () => {
    const chamadas = abrir()
    fireEvent.click((await screen.findAllByRole('button', { name: /Despachar/ }))[0])

    fireEvent.change(await screen.findByLabelText('Código de rastreio'), { target: { value: 'BR123' } })
    fireEvent.click(screen.getByLabelText('Transportadora'))
    fireEvent.click(await screen.findByRole('option', { name: 'Correios' }))
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Despachar' }))

    await waitFor(() => expect(chamadas.find((c) => c.metodo === 'POST')).toBeDefined())
    expect(chamadas.find((c) => c.metodo === 'POST')).toMatchObject({
      url: '/api/operadora/pedidos/p1/despacho',
      corpo: { lote: 7, retirada: false, transportadora: 'Correios', codigoDeRastreio: 'BR123' },
    })
  })

  it('na retirada não envia transportadora nem rastreio', async () => {
    const chamadas = abrir()
    fireEvent.click((await screen.findAllByRole('button', { name: /Despachar/ }))[0])
    fireEvent.change(await screen.findByLabelText('Código de rastreio'), { target: { value: 'BR123' } })
    fireEvent.click(screen.getByRole('button', { name: 'Pronto para retirada' }))

    await waitFor(() => expect(chamadas.find((c) => c.metodo === 'POST')).toBeDefined())
    expect(chamadas.find((c) => c.metodo === 'POST')?.corpo).toEqual({ lote: 7, retirada: true })
  })

  it('estorna o pedido com o motivo', async () => {
    const chamadas = abrir()
    fireEvent.pointerDown((await screen.findAllByRole('button', { name: /Ações de pedido de Loja Centro/ }))[0], { button: 0, ctrlKey: false })
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Estornar pedido' }))
    fireEvent.change(await screen.findByLabelText(/Motivo/), { target: { value: 'Pedido duplicado' } })
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Estornar pedido' }))

    await waitFor(() => expect(chamadas.find((c) => c.metodo === 'POST')).toBeDefined())
    expect(chamadas.find((c) => c.metodo === 'POST')).toMatchObject({ url: '/api/operadora/pedidos/p1/estorno', corpo: { motivo: 'Pedido duplicado' } })
  })
})
