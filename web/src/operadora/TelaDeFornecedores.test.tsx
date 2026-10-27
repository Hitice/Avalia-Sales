import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { useSessao } from '@/acesso/sessao'
import { TelaDeFornecedores } from './TelaDeFornecedores'

const fornecedor = { id: 'f1', nome: 'Gráfica Alfa', documento: '11222333000181', contato: '34 3333-0000', funcoes: ['Grafica', 'Confeccao'], observacao: null, ativo: true, usos: 2 }

afterEach(() => {
  vi.unstubAllGlobals()
  useSessao.getState().limpar()
})

function abrir() {
  const envios: { metodo: string; url: string; corpo: unknown }[] = []
  vi.stubGlobal('fetch', vi.fn(async (url: string, opcoes?: RequestInit) => {
    if (opcoes?.method && opcoes.method !== 'GET') {
      envios.push({ metodo: opcoes.method, url: String(url), corpo: opcoes.body ? JSON.parse(String(opcoes.body)) : null })
      return new Response(JSON.stringify(fornecedor), { status: 200 })
    }
    return new Response(JSON.stringify({ itens: [fornecedor], total: 1, proxima: null }), { status: 200 })
  }))
  render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><TelaDeFornecedores /></QueryClientProvider>)
  return envios
}

describe('TelaDeFornecedores', () => {
  it('mostra as funções com rótulo e o documento com máscara', async () => {
    abrir()
    await screen.findByText('Gráfica Alfa')
    expect(screen.getByText('Confecção com dado variável', { selector: '[data-slot=badge]' })).toBeInTheDocument()
    expect(screen.getByText('11.222.333/0001-81')).toBeInTheDocument()
    expect(screen.getByText('Ativo')).toBeInTheDocument()
  })

  it('editar carrega o cadastro e salva só com dígitos no documento', async () => {
    const envios = abrir()
    await screen.findByText('Gráfica Alfa')

    fireEvent.click(screen.getAllByRole('button', { name: /Editar/ })[0])
    expect(await screen.findByLabelText('CNPJ ou CPF')).toHaveValue('11.222.333/0001-81')
    fireEvent.click(screen.getByRole('button', { name: 'Salvar fornecedor' }))

    await waitFor(() => expect(envios).toHaveLength(1))
    expect(envios[0]).toMatchObject({ metodo: 'PUT', url: expect.stringContaining('/operadora/fornecedores/f1'), corpo: { nome: 'Gráfica Alfa', documento: '11222333000181', funcoes: ['Grafica', 'Confeccao'] } })
  })

  it('criar exige ao menos uma função', async () => {
    const envios = abrir()
    await screen.findByText('Gráfica Alfa')

    fireEvent.change(screen.getByLabelText('Nome'), { target: { value: 'Acrílicos Beta' } })
    fireEvent.click(screen.getByRole('button', { name: 'Criar fornecedor' }))
    expect(await screen.findByText('Marque o que o fornecedor faz.')).toBeInTheDocument()
    expect(envios).toHaveLength(0)

    fireEvent.click(screen.getByLabelText('Acrílico'))
    fireEvent.click(screen.getByRole('button', { name: 'Criar fornecedor' }))
    await waitFor(() => expect(envios).toHaveLength(1))
    expect(envios[0]).toMatchObject({ metodo: 'POST', corpo: { nome: 'Acrílicos Beta', documento: null, funcoes: ['Acrilico'] } })
  })
})
