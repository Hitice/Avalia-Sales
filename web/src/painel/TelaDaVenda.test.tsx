import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { useSessao } from '@/acesso/sessao'
import { TelaDaVenda } from './TelaDaVenda'

afterEach(() => {
  vi.unstubAllGlobals()
  useSessao.setState({ perfil: null })
})

const venda = {
  id: 'v1', codigo: 'ABC123', cliente: 'Padaria Sol', produto: 'Placa', vendedorId: 'u1', vendedor: 'Ju', valorCents: 10_000, comissaoCents: 1_000,
  meio: 'Pix', pagamento: null, vendidaEm: '2026-10-20T15:00:00Z', situacao: 'Registrada', cortesia: false, doSistemaAnterior: false,
}

function abrir(cliente: object, papel: 'Dono' | 'Vendedor' = 'Dono') {
  useSessao.setState({ perfil: { id: 'u1', papel, nome: 'Ju', email: 'ju@x.com', emailConfirmado: true, revendedor: 'Loja' } as never })
  const detalhe = { venda, cliente, cobranca: null, itens: [], ordens: [], razao: [], observacaoDoCancelamento: null, canceladaEm: null, venceEm: null, comissaoRepassada: false }
  vi.stubGlobal('fetch', vi.fn(async (url: string) => new Response(JSON.stringify(url.endsWith('/revenda') ? { revendedor: { encerradaEm: null } } : detalhe), { status: 200 })))
  render(
    <MemoryRouter initialEntries={['/painel/vendas/v1']}>
      <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
        <Routes><Route path="/painel/vendas/:id" element={<TelaDaVenda />} /></Routes>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

const cliente = { id: 'c1', nome: 'Padaria Sol', responsavel: 'Ana', telefone: '11988887777', documento: '12345678000190', email: 'ana@x.com' }

describe('cartão Cliente da venda', () => {
  it('o nome leva à ficha do cliente e não repete documento nem e-mail', async () => {
    abrir(cliente)
    const link = await screen.findByRole('link', { name: 'Padaria Sol' })
    expect(link).toHaveAttribute('href', '/painel/clientes?cliente=c1')
    expect(screen.queryByText('ana@x.com')).toBeNull()
    expect(screen.queryByText(/Falta CNPJ ou CPF/)).toBeNull()
    expect(screen.queryByRole('link', { name: 'Abrir em Clientes' })).toBeNull()
  })

  it('sem documento ou e-mail, avisa e leva a completar na ficha', async () => {
    abrir({ ...cliente, documento: null })
    expect(await screen.findByText(/Falta CNPJ ou CPF e e-mail para cobrar/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Completar na ficha' })).toHaveAttribute('href', '/painel/clientes?cliente=c1')
  })

  it('o vendedor vê o nome sem link para Clientes', async () => {
    abrir(cliente, 'Vendedor')
    await screen.findByText('Responsável')
    expect(screen.queryByRole('link', { name: 'Padaria Sol' })).toBeNull()
  })
})
