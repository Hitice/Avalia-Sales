import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { TelaDeComissoes } from './TelaDeComissoes'

const saldo = { vendedorId: 'v1', vendedor: 'Ju Gomes', chavePix: null, devidaCents: 30_000, repassadaCents: 0, abertaCents: 30_000, ultimoRepasse: null }

const pendente = { cobrancaId: 'c1', vendaId: 'x1', vendedorId: 'v1', vendedor: 'Ju Gomes', cliente: 'Padaria Sol', meio: 'Dinheiro', valorCents: 10_000, comissaoCents: 2_000, recebidoEm: '2026-10-20T15:00:00Z' }

afterEach(() => {
  vi.unstubAllGlobals()
})

function abrir(pendentes: unknown[] = []) {
  const envios: unknown[] = []
  vi.stubGlobal('fetch', vi.fn(async (url: string, opcoes?: RequestInit) => {
    if (opcoes?.method === 'POST') {
      envios.push(JSON.parse(String(opcoes.body)))
      return new Response('{}', { status: 200 })
    }
    const vazia = { itens: [], total: 0, proxima: null }
    return new Response(JSON.stringify(url.endsWith('/comissoes') ? [saldo] : url.includes('/comissoes/conferencia') ? pendentes : url.includes('/repasses') ? vazia : []), { status: 200 })
  }))
  render(<MemoryRouter><QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><TelaDeComissoes /></QueryClientProvider></MemoryRouter>)
  return envios
}

describe('TelaDeComissoes', () => {
  it('com data limite, o botão não promete o saldo todo: o valor pago é o das vendas até a data', async () => {
    abrir()
    await screen.findByText('Ju Gomes')
    fireEvent.click(screen.getByRole('button', { name: 'Registrar repasse' }))
    const dialogo = await screen.findByRole('dialog')
    expect(within(dialogo).getByRole('button', { name: /Registrar R\$\s?300,00/ })).toBeInTheDocument()

    fireEvent.change(within(dialogo).getByLabelText('Vendas pagas até'), { target: { value: '2026-10-15' } })

    expect(within(dialogo).queryByRole('button', { name: /Registrar R\$/ })).toBeNull()
    expect(within(dialogo).getByRole('button', { name: 'Registrar repasse' })).toBeInTheDocument()
  })

  it('a data limite vai à API como o fim do dia em Brasília', async () => {
    const envios = abrir()
    await screen.findByText('Ju Gomes')
    fireEvent.click(screen.getByRole('button', { name: 'Registrar repasse' }))
    const dialogo = await screen.findByRole('dialog')
    fireEvent.change(within(dialogo).getByLabelText('Vendas pagas até'), { target: { value: '2026-10-15' } })

    fireEvent.click(within(dialogo).getByRole('button', { name: 'Registrar repasse' }))

    await waitFor(() => expect(envios).toHaveLength(1))
    expect(envios[0]).toMatchObject({ vendedorId: 'v1', ate: '2026-10-16T02:59:59.000Z' })
  })

  it('o dinheiro recebido pelo vendedor é confirmado por linha ou todo de uma vez', async () => {
    const envios = abrir([pendente])
    expect(await screen.findByText('A confirmar')).toBeInTheDocument()
    expect(screen.getByText('Dinheiro')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Confirmar todos (1)' }))
    await waitFor(() => expect(envios).toHaveLength(1))
    expect(envios[0]).toEqual({ cobrancaIds: null })
  })

  it('a ação da linha confirma só aquele recebimento', async () => {
    const envios = abrir([pendente])
    fireEvent.click((await screen.findAllByRole('button', { name: /Confirmei o dinheiro/ }))[0])
    await waitFor(() => expect(envios).toHaveLength(1))
    expect(envios[0]).toEqual({ cobrancaIds: ['c1'] })
  })
})
