import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { TelaDeCadastro } from './TelaDeCadastro'
import { VerifiqueSeuEmail } from './VerifiqueSeuEmail'

function resposta(status: number, corpo: unknown = {}) {
  return new Response(status === 204 ? null : JSON.stringify(corpo), { status, headers: { 'Content-Type': 'application/json' } })
}

afterEach(() => {
  vi.unstubAllGlobals()
  vi.useRealTimers()
})

/** Um segundo por vez: cada tick da contagem só se agenda depois de a tela redesenhar. */
async function passarUmMinuto() {
  for (let i = 0; i < 61; i++) await act(() => vi.advanceTimersByTimeAsync(1000))
}

async function preencher(usuario: ReturnType<typeof userEvent.setup>) {
  await usuario.type(screen.getByLabelText('CNPJ'), '13186091000143')
  await usuario.type(screen.getByLabelText('Empresa'), 'Revenda Nova')
  await usuario.type(screen.getByLabelText('Celular com DDD'), '11987654321')
  await usuario.type(screen.getByLabelText('CEP'), '38400192')
  await usuario.type(screen.getByLabelText('Rua ou avenida'), 'Avenida Princesa Isabel')
  await usuario.type(screen.getByLabelText('Número'), '100')
  await usuario.type(screen.getByLabelText('Bairro'), 'Tabajaras')
  await usuario.type(screen.getByLabelText('Cidade'), 'Uberlândia')
  await usuario.type(screen.getByLabelText('UF'), 'MG')
  await usuario.type(screen.getByLabelText('Seu nome'), 'Ana Dona')
  await usuario.type(screen.getByLabelText('E-mail'), 'ana@revenda.dev')
}

// O formulário tem onze campos e ~120 teclas. Sem a pausa entre teclas (delay: null) e com folga de tempo, o teste
// não depende da velocidade do executor: no CI, com o Chromium das histórias em paralelo, ele passou de 5 s.
const TEMPO_DO_PREENCHIMENTO = 20_000

describe('TelaDeCadastro', () => {
  it('não pede senha, manda o cadastro e passa ao aviso de verificar o e-mail', async () => {
    const fetch = vi.fn((url: string) => Promise.resolve(url.startsWith('/api/cadastro/') ? resposta(404) : resposta(202, { mensagem: 'Enviamos um e-mail para continuar.' })))
    vi.stubGlobal('fetch', fetch)
    const usuario = userEvent.setup({ delay: null })
    render(<MemoryRouter><TelaDeCadastro /></MemoryRouter>)

    expect(screen.queryByLabelText('Senha')).not.toBeInTheDocument()
    await preencher(usuario)
    await usuario.click(screen.getByRole('button', { name: 'Cadastrar' }))

    expect(await screen.findByRole('heading', { name: 'Verifique seu e-mail' })).toBeInTheDocument()
    expect(screen.getByText('ana@revenda.dev')).toBeInTheDocument()
    const envio = fetch.mock.calls.find(([url]) => url === '/api/cadastro') as unknown as [string, RequestInit]
    const corpo = JSON.parse(envio[1].body as string) as Record<string, unknown>
    expect(corpo).toMatchObject({ cnpj: '13186091000143', email: 'ana@revenda.dev', celular: '11987654321' })
    expect(corpo).not.toHaveProperty('senha')
  }, TEMPO_DO_PREENCHIMENTO)

  it('mostra a recusa da API no formulário e mantém o que foi digitado', async () => {
    vi.stubGlobal('fetch', vi.fn((url: string) => Promise.resolve(url.startsWith('/api/cadastro/') ? resposta(404) : resposta(503, { detail: 'Recebemos muitos cadastros agora. Tente de novo mais tarde.' }))))
    const usuario = userEvent.setup({ delay: null })
    render(<MemoryRouter><TelaDeCadastro /></MemoryRouter>)

    await preencher(usuario)
    await usuario.click(screen.getByRole('button', { name: 'Cadastrar' }))

    expect(await screen.findByText('Recebemos muitos cadastros agora. Tente de novo mais tarde.')).toBeInTheDocument()
    expect(screen.getByLabelText('E-mail')).toHaveValue('ana@revenda.dev')
  }, TEMPO_DO_PREENCHIMENTO)
})

describe('VerifiqueSeuEmail', () => {
  it('reenvia só depois da espera de 1 minuto e mostra o erro da API', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    const fetch = vi.fn()
      .mockResolvedValueOnce(resposta(204))
      .mockResolvedValueOnce(resposta(503, { detail: 'Recebemos muitos cadastros agora. Tente de novo mais tarde.' }))
    vi.stubGlobal('fetch', fetch)
    const usuario = userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
    render(<VerifiqueSeuEmail email="ana@revenda.dev" aoVoltar={vi.fn()} />)

    const botao = screen.getByRole('button', { name: /Reenviar em/ })
    expect(botao).toBeDisabled()

    await passarUmMinuto()
    await usuario.click(screen.getByRole('button', { name: 'Reenviar e-mail' }))
    expect(await screen.findByRole('status')).toHaveTextContent('Pedido feito')
    expect(JSON.parse((fetch.mock.calls[0] as [string, RequestInit])[1].body as string)).toEqual({ email: 'ana@revenda.dev' })
    expect(screen.getByRole('button', { name: /Reenviar em/ })).toBeDisabled()

    await passarUmMinuto()
    await usuario.click(screen.getByRole('button', { name: 'Reenviar e-mail' }))
    expect(await screen.findByText('Recebemos muitos cadastros agora. Tente de novo mais tarde.')).toBeInTheDocument()
  })

  it('volta ao formulário para corrigir o e-mail', async () => {
    const voltar = vi.fn()
    const usuario = userEvent.setup()
    render(<VerifiqueSeuEmail email="errado@revenda.dev" aoVoltar={voltar} />)

    await usuario.click(screen.getByRole('button', { name: 'Digitei o e-mail errado' }))

    expect(voltar).toHaveBeenCalledOnce()
  })
})
