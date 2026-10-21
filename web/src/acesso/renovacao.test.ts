import { afterEach, describe, expect, it, vi } from 'vitest'
import { renovarSessao } from './renovacao'
import { useSessao } from './sessao'

const perfil = { id: '1', nome: 'Dona', email: 'dona@teste.com.br', papel: 'Dono' as const, revendedorId: 'r', revendedor: 'Revenda' }

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
})
