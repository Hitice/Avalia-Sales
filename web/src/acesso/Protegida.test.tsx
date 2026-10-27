import { render, screen } from '@testing-library/react'
import { createMemoryRouter, RouterProvider, useLocation } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { Protegida } from './Protegida'
import { type Papel, destinoDaVolta, useSessao } from './sessao'

function Entrada() {
  const local = useLocation()
  return <p>Entrada{local.search}</p>
}

function abrir(papel: Papel | null, iniciada = true, inicio = '/operadora') {
  useSessao.setState({ iniciada })
  if (papel) {
    useSessao.getState().entrar({
      token: 't',
      expiraEm: new Date(Date.now() + 900_000).toISOString(),
      perfil: { id: '1', nome: 'X', email: 'x@teste.com.br', papel, revendedorId: null, revendedor: null, emailConfirmado: true },
    })
  }

  const roteador = createMemoryRouter([
    { path: '/entrar', element: <Entrada /> },
    { path: '/vendedor/inicio', element: <p>Painel do vendedor</p> },
    { element: <Protegida papeis={['Operadora']} />, children: [{ path: '/operadora', element: <p>Operação</p> }] },
  ], { initialEntries: [inicio] })
  render(<RouterProvider router={roteador} />)
}

afterEach(() => {
  vi.unstubAllGlobals()
  useSessao.getState().limpar()
  useSessao.setState({ iniciada: false })
})

const perfil = { id: '1', nome: 'X', email: 'x@teste.com.br', papel: 'Operadora' as const, revendedorId: null, revendedor: null, emailConfirmado: true }

/** A aba ficou parada: o token de 15 minutos venceu, e o cookie de renovação pode ainda valer. */
function abrirComTokenVencido(renovacao: Response) {
  useSessao.setState({ iniciada: true })
  useSessao.getState().entrar({ token: 'velho', expiraEm: new Date(Date.now() - 1_000).toISOString(), perfil })
  vi.stubGlobal('fetch', vi.fn(async () => renovacao.clone()))
  const roteador = createMemoryRouter([
    { path: '/entrar', element: <Entrada /> },
    { element: <Protegida papeis={['Operadora']} />, children: [{ path: '/operadora', element: <p>Operação</p> }] },
  ], { initialEntries: ['/operadora'] })
  render(<RouterProvider router={roteador} />)
}

describe('Protegida', () => {
  it('espera a renovação pelo cookie antes de mandar para a entrada', () => {
    abrir(null, false)
    expect(screen.getByRole('status')).toHaveTextContent('Carregando')
  })

  it('token vencido com o cookie valendo renova e fica na tela', async () => {
    abrirComTokenVencido(new Response(JSON.stringify({ token: 'novo', expiraEm: new Date(Date.now() + 900_000).toISOString(), perfil }), { status: 200 }))

    expect(await screen.findByText('Operação')).toBeInTheDocument()
    expect(useSessao.getState().token).toBe('novo')
  })

  it('token vencido e o cookie recusado vai para a entrada', async () => {
    abrirComTokenVencido(new Response(null, { status: 401 }))

    expect(await screen.findByText(`Entrada?volta=${encodeURIComponent('/operadora')}`)).toBeInTheDocument()
  })

  it('sem sessão, vai para a entrada levando a tela pedida, com a busca', () => {
    abrir(null, true, '/operadora?codigo=K7M2PX')
    expect(screen.getByText(`Entrada?volta=${encodeURIComponent('/operadora?codigo=K7M2PX')}`)).toBeInTheDocument()
  })

  it('a volta só aceita caminho interno', () => {
    expect(destinoDaVolta('/vender?codigo=K7M2PX')).toBe('/vender?codigo=K7M2PX')
    expect(destinoDaVolta('https://outro.site')).toBeNull()
    expect(destinoDaVolta('//outro.site/x')).toBeNull()
    expect(destinoDaVolta('/entrar')).toBeNull()
    expect(destinoDaVolta(null)).toBeNull()
    expect(destinoDaVolta('/\t/evil.com')).toBeNull()
    expect(destinoDaVolta('/%09/evil.com')).toBe('/%09/evil.com')
    expect(destinoDaVolta('/\\evil.com')).toBeNull()
    expect(destinoDaVolta('/vender?codigo=K7M2PX#topo')).toBe('/vender?codigo=K7M2PX#topo')
  })

  it('papel errado vai para a área do próprio papel', () => {
    abrir('Vendedor')
    expect(screen.getByText('Painel do vendedor')).toBeInTheDocument()
  })

  it('papel certo abre a área', () => {
    abrir('Operadora')
    expect(screen.getByText('Operação')).toBeInTheDocument()
  })
})
