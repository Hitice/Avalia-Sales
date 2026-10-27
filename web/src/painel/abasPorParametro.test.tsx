import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { TelaDoFinanceiro } from '@/financeiro/TelaDoFinanceiro'
import { TelaDeConfiguracoes } from './TelaDeConfiguracoes'

afterEach(() => {
  vi.unstubAllGlobals()
})

const revenda = { revendedor: { comissaoPadraoBps: 1000, renovacaoBps: 6000, encerradaEm: null }, contrato: 'Assinado' }

function abrir(tela: React.ReactNode, endereco: string) {
  vi.stubGlobal('fetch', vi.fn(async (url: string) => new Response(JSON.stringify(url.endsWith('/revenda') ? revenda : url.includes('/razao') ? { itens: [], total: 0, proxima: null } : []), { status: 200 })))
  render(<MemoryRouter initialEntries={[endereco]}><QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>{tela}</QueryClientProvider></MemoryRouter>)
}

describe('abas pelo parâmetro aba', () => {
  it('Configurações abre na Marca com ?aba=marca e em Geral sem parâmetro', async () => {
    abrir(<TelaDeConfiguracoes />, '/painel/configuracoes?aba=marca')
    expect(await screen.findByRole('tab', { name: 'Marca' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByRole('tab', { name: 'Geral' })).toHaveAttribute('aria-selected', 'false')
  })

  it('Configurações abre em Geral por padrão, com o encerramento da conta', async () => {
    abrir(<TelaDeConfiguracoes />, '/painel/configuracoes')
    expect(await screen.findByRole('tab', { name: 'Geral' })).toHaveAttribute('aria-selected', 'true')
    expect(await screen.findByText('Comissão padrão (%)')).toBeInTheDocument()
  })

  it('Financeiro abre em Lançamentos com ?aba=lancamentos', async () => {
    abrir(<TelaDoFinanceiro />, '/painel/financeiro?aba=lancamentos')
    expect(await screen.findByRole('tab', { name: 'Lançamentos' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByRole('tab', { name: 'Resultado' })).toHaveAttribute('aria-selected', 'false')
  })
})
