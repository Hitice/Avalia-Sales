import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { TelaDeComissoes } from './TelaDeComissoes'

const saldo = { vendedorId: 'v1', vendedor: 'Ju Gomes', chavePix: null, devidaCents: 30_000, repassadaCents: 0, abertaCents: 30_000, ultimoRepasse: null }

afterEach(() => {
  vi.unstubAllGlobals()
})

function abrir() {
  const envios: unknown[] = []
  vi.stubGlobal('fetch', vi.fn(async (url: string, opcoes?: RequestInit) => {
    if (opcoes?.method === 'POST') {
      envios.push(JSON.parse(String(opcoes.body)))
      return new Response('{}', { status: 200 })
    }
    return new Response(JSON.stringify(url.endsWith('/comissoes') ? [saldo] : []), { status: 200 })
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
})
