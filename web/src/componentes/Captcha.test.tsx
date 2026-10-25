import { render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { Captcha } from './Captcha'

type Opcoes = { sitekey: string; callback: (token: string) => void; 'expired-callback': () => void; 'error-callback': () => void }

function falsoTurnstile() {
  const render = vi.fn<(elemento: HTMLElement, opcoes: Opcoes) => string>().mockReturnValue('widget-1')
  const reset = vi.fn()
  const remove = vi.fn()
  window.turnstile = { render, reset, remove } as unknown as typeof window.turnstile
  return { render, reset, remove }
}

afterEach(() => {
  delete window.turnstile
})

describe('Captcha', () => {
  it('desenha o widget com a chave e entrega o token resolvido e o vencimento', async () => {
    const turnstile = falsoTurnstile()
    const aoMudar = vi.fn()
    render(<Captcha chave="chave-do-site" aoMudar={aoMudar} />)

    await waitFor(() => expect(turnstile.render).toHaveBeenCalledOnce())
    const opcoes = turnstile.render.mock.calls[0][1]
    expect(opcoes.sitekey).toBe('chave-do-site')
    opcoes.callback('token-1')
    opcoes['expired-callback']()

    expect(aoMudar.mock.calls).toEqual([['token-1'], [null]])
  })

  it('pede desafio novo quando o reinício muda e remove o widget ao sair', async () => {
    const turnstile = falsoTurnstile()
    const { rerender, unmount } = render(<Captcha chave="c" aoMudar={vi.fn()} reinicio={0} />)
    await waitFor(() => expect(turnstile.render).toHaveBeenCalled())

    rerender(<Captcha chave="c" aoMudar={vi.fn()} reinicio={1} />)
    expect(turnstile.reset).toHaveBeenCalledWith('widget-1')

    unmount()
    expect(turnstile.remove).toHaveBeenCalledWith('widget-1')
  })

  it('avisa quando a Cloudflare dá erro, sem deixar o token anterior valer', async () => {
    const turnstile = falsoTurnstile()
    const aoMudar = vi.fn()
    render(<Captcha chave="c" aoMudar={aoMudar} />)
    await waitFor(() => expect(turnstile.render).toHaveBeenCalled())

    turnstile.render.mock.calls[0][1]['error-callback']()

    expect(await screen.findByText(/Não foi possível carregar a verificação de segurança/)).toBeInTheDocument()
    expect(aoMudar).toHaveBeenLastCalledWith(null)
  })
})
