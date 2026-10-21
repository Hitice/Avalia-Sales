import { render, screen } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { afterEach, describe, expect, it } from 'vitest'
import { Protegida } from './Protegida'
import { type Papel, useSessao } from './sessao'

function abrir(papel: Papel | null, iniciada = true) {
  useSessao.setState({ iniciada })
  if (papel) {
    useSessao.getState().entrar({
      token: 't',
      expiraEm: new Date(Date.now() + 900_000).toISOString(),
      perfil: { id: '1', nome: 'X', email: 'x@teste.com.br', papel, revendedorId: null, revendedor: null },
    })
  }

  const roteador = createMemoryRouter([
    { path: '/entrar', element: <p>Entrada</p> },
    { path: '/vender', element: <p>Nova venda</p> },
    { element: <Protegida papeis={['Operadora']} />, children: [{ path: '/operadora', element: <p>Operação</p> }] },
  ], { initialEntries: ['/operadora'] })
  render(<RouterProvider router={roteador} />)
}

afterEach(() => {
  useSessao.getState().limpar()
  useSessao.setState({ iniciada: false })
})

describe('Protegida', () => {
  it('espera a renovação pelo cookie antes de mandar para a entrada', () => {
    abrir(null, false)
    expect(screen.getByRole('status')).toHaveTextContent('Carregando')
  })

  it('sem sessão, vai para a entrada', () => {
    abrir(null)
    expect(screen.getByText('Entrada')).toBeInTheDocument()
  })

  it('papel errado vai para a área do próprio papel', () => {
    abrir('Vendedor')
    expect(screen.getByText('Nova venda')).toBeInTheDocument()
  })

  it('papel certo abre a área', () => {
    abrir('Operadora')
    expect(screen.getByText('Operação')).toBeInTheDocument()
  })
})
