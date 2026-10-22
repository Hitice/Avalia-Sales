import { afterEach, describe, expect, it, vi } from 'vitest'
import { renovarSessao } from './renovacao'
import { useSessao } from './sessao'

const perfil = { id: '1', nome: 'Dona', email: 'dona@teste.com.br', papel: 'Dono' as const, revendedorId: 'r', revendedor: 'Revenda' , emailConfirmado: true }

afterEach(() => {
  vi.unstubAllGlobals()
  useSessao.getState().limpar()
})

describe('renovarSessao', () => {
  it('chamadas ao mesmo tempo usam uma renovação só, porque o refresh é rotativo', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ token: 't', expiraEm: new Date(Date.now() + 900_000).toISOString(), perfil }), { status: 200 }))
    vi.stubGlobal('fetch', fetch)

    const [a, b] = await Promise.all([renovarSessao(), renovarSessao()])

    expect(a && b).toBe(true)
    expect(fetch).toHaveBeenCalledTimes(1)
    expect(useSessao.getState().perfil?.papel).toBe('Dono')
  })

  it('cookie recusado limpa a sessão', async () => {
    useSessao.getState().entrar({ token: 'velho', expiraEm: new Date(Date.now() + 900_000).toISOString(), perfil })
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 401 })))

    expect(await renovarSessao()).toBe(false)
    expect(useSessao.getState().token).toBeNull()
  })

  it('limite de pedidos não derruba a sessão: espera e tenta de novo', async () => {
    vi.useFakeTimers()
    useSessao.getState().entrar({ token: 'velho', expiraEm: new Date(Date.now() + 900_000).toISOString(), perfil })
    const fetch = vi.fn()
      .mockResolvedValueOnce(new Response(null, { status: 429 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ token: 'novo', expiraEm: new Date(Date.now() + 900_000).toISOString(), perfil }), { status: 200 }))
    vi.stubGlobal('fetch', fetch)

    const renovada = renovarSessao()
    await vi.advanceTimersByTimeAsync(2_000)

    expect(await renovada).toBe(true)
    expect(useSessao.getState().token).toBe('novo')
    vi.useRealTimers()
  })

  it('servidor fora do ar mantém o token que havia', async () => {
    vi.useFakeTimers()
    useSessao.getState().entrar({ token: 'velho', expiraEm: new Date(Date.now() + 900_000).toISOString(), perfil })
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 503 })))

    const renovada = renovarSessao()
    await vi.advanceTimersByTimeAsync(2_000)

    expect(await renovada).toBe(false)
    expect(useSessao.getState().token).toBe('velho')
    vi.useRealTimers()
  })
})
