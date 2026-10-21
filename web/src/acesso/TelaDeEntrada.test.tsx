import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { TelaDeEntrada } from './TelaDeEntrada'
import { useSessao } from './sessao'

function abrir() {
  const roteador = createMemoryRouter(
    [{ path: '/entrar', element: <TelaDeEntrada /> }, { path: '/painel', element: <p>Painel aberto</p> }],
    { initialEntries: ['/entrar'] },
  )
  render(<RouterProvider router={roteador} />)
  return userEvent.setup()
}

function respostaDaApi(status: number, corpo: unknown) {
  return vi.fn().mockResolvedValue(new Response(JSON.stringify(corpo), { status, headers: { 'Content-Type': 'application/json' } }))
}

afterEach(() => {
  vi.unstubAllGlobals()
  useSessao.getState().limpar()
})

describe('TelaDeEntrada', () => {
  it('pede e-mail válido e senha antes de chamar a API', async () => {
    const fetch = respostaDaApi(200, {})
    vi.stubGlobal('fetch', fetch)
    const usuario = abrir()

    await usuario.type(screen.getByLabelText('E-mail'), 'nao-e-email')
    await usuario.click(screen.getByRole('button', { name: 'Entrar' }))

    expect(await screen.findByText('Digite um e-mail válido.')).toBeInTheDocument()
    expect(screen.getByText('Digite a senha.')).toBeInTheDocument()
    expect(screen.getByLabelText('E-mail')).toHaveAttribute('aria-invalid', 'true')
    expect(fetch).not.toHaveBeenCalled()
  })

  it('senha errada mostra a mesma mensagem que e-mail inexistente', async () => {
    vi.stubGlobal('fetch', respostaDaApi(401, {}))
    const usuario = abrir()

    await usuario.type(screen.getByLabelText('E-mail'), 'dono@revenda-a.dev')
    await usuario.type(screen.getByLabelText('Senha'), 'senha-errada-123')
    await usuario.click(screen.getByRole('button', { name: 'Entrar' }))

    expect(await screen.findByText('E-mail ou senha não conferem.')).toBeInTheDocument()
  })

  it('login certo guarda a sessão e abre o painel', async () => {
    const fetch = respostaDaApi(200, { token: 'token-de-teste', expiraEm: new Date(Date.now() + 900_000).toISOString() })
    vi.stubGlobal('fetch', fetch)
    const usuario = abrir()

    await usuario.type(screen.getByLabelText('E-mail'), 'dono@revenda-a.dev')
    await usuario.type(screen.getByLabelText('Senha'), 'demonstracao-123')
    await usuario.click(screen.getByRole('button', { name: 'Entrar' }))

    expect(await screen.findByText('Painel aberto')).toBeInTheDocument()
    expect(useSessao.getState().token).toBe('token-de-teste')
    const [caminho, opcoes] = fetch.mock.calls[0] as [string, RequestInit]
    expect(caminho).toBe('/api/auth/entrar')
    expect(JSON.parse(opcoes.body as string)).toEqual({ email: 'dono@revenda-a.dev', senha: 'demonstracao-123' })
  })
})
