import { afterEach, describe, expect, it, vi } from 'vitest'
import { useSessao } from '@/acesso/sessao'
import { ErroDaApi, chamarApi, enviarJson, mensagemDoErro, novaChaveDeIdempotencia } from './cliente'

const perfil = { id: '1', nome: 'Dona', email: 'dona@teste.com.br', papel: 'Dono' as const, revendedorId: 'r', revendedor: 'Revenda', emailConfirmado: true }
const umaHoraAdiante = () => new Date(Date.now() + 3_600_000).toISOString()
const emitido = (token: string) => ({ token, expiraEm: new Date(Date.now() + 900_000).toISOString(), perfil })
const resposta = (corpo: unknown, status = 200) => new Response(typeof corpo === 'string' ? corpo : JSON.stringify(corpo), { status })

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
  useSessao.getState().limpar()
})

describe('chamarApi: sessão', () => {
  it('401 e a renovação indisponível (503) não encerram a sessão: o cookie ainda vale', async () => {
    vi.useFakeTimers()
    useSessao.getState().entrar(emitido('velho'))
    const fetch = vi.fn(async (url: string) => (url === '/api/auth/renovar' ? resposta(null, 503) : resposta({ title: 'Unauthorized' }, 401)))
    vi.stubGlobal('fetch', fetch)

    const chamada = chamarApi('/vendas').catch((e: unknown) => e)
    await vi.advanceTimersByTimeAsync(3_000)
    const erro = await chamada

    expect(erro).toBeInstanceOf(ErroDaApi)
    expect(useSessao.getState().token).toBe('velho')
  })

  it('401 e o cookie recusado encerram a sessão', async () => {
    useSessao.getState().entrar(emitido('velho'))
    vi.stubGlobal('fetch', vi.fn(async (url: string) => (url === '/api/auth/renovar' ? resposta(null, 401) : resposta({}, 401))))

    await expect(chamarApi('/vendas')).rejects.toBeInstanceOf(ErroDaApi)

    expect(useSessao.getState().token).toBeNull()
  })

  it('401, renovação boa e o mesmo pedido de novo com o token novo', async () => {
    useSessao.getState().entrar(emitido('velho'))
    const fetch = vi.fn(async (url: string, opcoes?: RequestInit) => {
      if (url === '/api/auth/renovar') return resposta(emitido('novo'))
      return new Headers(opcoes?.headers).get('Authorization') === 'Bearer novo' ? resposta({ ok: true }) : resposta({}, 401)
    })
    vi.stubGlobal('fetch', fetch)

    expect(await chamarApi('/vendas')).toEqual({ ok: true })
    expect(useSessao.getState().token).toBe('novo')
  })
})

describe('chamarApi: erros na tela', () => {
  it.each([
    [403, {}, 'Seu acesso não abre esta área.'],
    [422, { detail: 'A placa já foi vendida.' }, 'A placa já foi vendida.'],
    [429, {}, 'Muitas tentativas. Espere um minuto e tente de novo.'],
    [500, { title: 'An error occurred' }, 'Não deu certo. Tente de novo.'],
  ])('%i vira texto para a pessoa, sem objeto cru', async (status, corpo, esperado) => {
    vi.stubGlobal('fetch', vi.fn(async () => resposta(corpo, status)))
    const erro = await chamarApi('/x').catch((e: unknown) => e)
    expect(mensagemDoErro(erro)).toBe(esperado)
  })

  it('502 do proxy, com página HTML, também vira a mensagem padrão', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => resposta('<html>Bad Gateway</html>', 502)))
    expect(mensagemDoErro(await chamarApi('/x').catch((e: unknown) => e))).toBe('Não deu certo. Tente de novo.')
  })

  it('rede caída vira "sem conexão"', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => { throw new TypeError('Failed to fetch') }))
    expect(mensagemDoErro(await chamarApi('/x').catch((e: unknown) => e))).toBe('Sem conexão com o servidor.')
  })
})

describe('chamarApi: gravação repetida', () => {
  const gravar = (corpo: unknown, caminho = '/financeiro/despesas') => chamarApi(caminho, { method: 'POST', body: JSON.stringify(corpo) })

  it('o mesmo POST enquanto o primeiro não voltou (toque duplo) vai uma vez só e as duas chamadas recebem a resposta', async () => {
    let soltar: () => void = () => undefined
    const fetch = vi.fn(() => new Promise<Response>((resolver) => { soltar = () => resolver(resposta({ id: 'd1' }, 201)) }))
    vi.stubGlobal('fetch', fetch)

    const a = gravar({ valorCents: 1000 })
    const b = gravar({ valorCents: 1000 })
    soltar()

    expect(await Promise.all([a, b])).toEqual([{ id: 'd1' }, { id: 'd1' }])
    expect(fetch).toHaveBeenCalledTimes(1)
  })

  it('corpo diferente, ou outro caminho, são gravações diferentes', async () => {
    const fetch = vi.fn(async () => resposta({}, 201))
    vi.stubGlobal('fetch', fetch)

    await Promise.all([gravar({ valorCents: 1000 }), gravar({ valorCents: 2000 }), gravar({ valorCents: 1000 }, '/financeiro/retiradas')])

    expect(fetch).toHaveBeenCalledTimes(3)
  })

  it('depois que a primeira terminou, repetir é uma gravação nova', async () => {
    const fetch = vi.fn(async () => resposta({}, 201))
    vi.stubGlobal('fetch', fetch)

    await gravar({ valorCents: 1000 })
    await gravar({ valorCents: 1000 })

    expect(fetch).toHaveBeenCalledTimes(2)
  })

  it('consulta (GET) nunca é unida: quem pede de novo quer o dado de agora', async () => {
    const fetch = vi.fn(async () => resposta([]))
    vi.stubGlobal('fetch', fetch)

    await Promise.all([chamarApi('/vendas'), chamarApi('/vendas')])

    expect(fetch).toHaveBeenCalledTimes(2)
  })

  it('a falha da primeira chega às duas', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => resposta({ detail: 'Venda recusada.' }, 422)))

    const resultados = await Promise.allSettled([gravar({ a: 1 }), gravar({ a: 1 })])

    expect(resultados.map((r) => r.status)).toEqual(['rejected', 'rejected'])
  })
})

const chaveDe = (chamada: unknown[]) => new Headers((chamada[1] as RequestInit).headers).get('Idempotency-Key')

describe('Idempotency-Key', () => {
  it('toda gravação POST leva uma chave, e cada ação do usuário leva uma chave nova', async () => {
    useSessao.getState().entrar({ token: 't', expiraEm: umaHoraAdiante(), perfil })
    const fetch = vi.fn().mockImplementation(() => Promise.resolve(new Response('{}', { status: 201 })))
    vi.stubGlobal('fetch', fetch)

    await chamarApi('/financeiro/despesas', enviarJson('POST', { descricao: 'a' }))
    await chamarApi('/financeiro/despesas', enviarJson('POST', { descricao: 'a' }))

    const [primeira, segunda] = fetch.mock.calls.map(chaveDe)
    expect(primeira).toMatch(/^[0-9a-f-]{36}$/)
    expect(segunda).toMatch(/^[0-9a-f-]{36}$/)
    expect(primeira).not.toBe(segunda)
  })

  it('o reenvio depois do 401 e da renovação da sessão usa a mesma chave', async () => {
    useSessao.getState().entrar({ token: 'velho', expiraEm: umaHoraAdiante(), perfil })
    const fetch = vi.fn()
      .mockResolvedValueOnce(new Response(null, { status: 401 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ token: 'novo', expiraEm: umaHoraAdiante(), perfil }), { status: 200 }))
      .mockResolvedValueOnce(new Response('{}', { status: 201 }))
    vi.stubGlobal('fetch', fetch)

    await chamarApi('/vendas', enviarJson('POST', { codigo: 'x' }))

    expect(fetch).toHaveBeenCalledTimes(3)
    const [primeiroEnvio, renovacao, reenvio] = fetch.mock.calls
    expect(chaveDe(primeiroEnvio)).toBeTruthy()
    expect(chaveDe(reenvio)).toBe(chaveDe(primeiroEnvio))
    expect(String(renovacao[0])).toBe('/api/auth/renovar')
    expect(new Headers((reenvio[1] as RequestInit).headers).get('Authorization')).toBe('Bearer novo')
  })

  it('leitura, alteração e exclusão não levam chave', async () => {
    useSessao.getState().entrar({ token: 't', expiraEm: umaHoraAdiante(), perfil })
    const fetch = vi.fn().mockImplementation(() => Promise.resolve(new Response('{}', { status: 200 })))
    vi.stubGlobal('fetch', fetch)

    await chamarApi('/financeiro/despesas')
    await chamarApi('/financeiro/despesas/1', enviarJson('PUT', { descricao: 'b' }))
    await chamarApi('/financeiro/despesas/1', enviarJson('DELETE'))

    expect(fetch.mock.calls.map(chaveDe)).toEqual([null, null, null])
  })

  it('sem crypto.randomUUID (HTTP na rede local) a chave sai do mesmo jeito', () => {
    const original = globalThis.crypto
    vi.stubGlobal('crypto', { getRandomValues: (bytes: Uint8Array<ArrayBuffer>) => original.getRandomValues(bytes) })

    expect(novaChaveDeIdempotencia()).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/)
  })
})
