import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, renderHook, waitFor } from '@testing-library/react'
import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { chamarApi } from '@/api/cliente'
import { paraQuery, TAMANHO_DA_PAGINA, useListaPaginada } from './paginacao'

vi.mock('@/api/cliente', () => ({ chamarApi: vi.fn() }))

const api = vi.mocked(chamarApi)

type Item = { id: string }
const pagina = (ids: string[], total = ids.length, proxima: number | null = null) => ({ itens: ids.map((id) => ({ id })), total, proxima })

function montar(opcoes?: Parameters<typeof useListaPaginada<Item>>[2]) {
  const cliente = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const envolver = ({ children }: { children: ReactNode }) => <QueryClientProvider client={cliente}>{children}</QueryClientProvider>
  return renderHook((props: { filtros?: Record<string, string | undefined> }) => useListaPaginada<Item>(['teste'], '/lista', { ...opcoes, filtros: props.filtros }), {
    wrapper: envolver,
    initialProps: {},
  })
}

/** Uma resposta que só chega quando o teste mandar. */
function adiada<T>() {
  let resolver!: (valor: T) => void
  const promessa = new Promise<T>((r) => { resolver = r })
  return { promessa, resolver }
}

beforeEach(() => {
  api.mockReset()
})

afterEach(() => {
  vi.useRealTimers()
})

describe('paraQuery', () => {
  it('deixa de fora o que está vazio e mantém false e 0', () => {
    expect(paraQuery({ busca: '', pagina: 1, estado: undefined, soAtivos: false, de: null, zero: 0 })).toBe('pagina=1&soAtivos=false&zero=0')
  })
})

describe('useListaPaginada', () => {
  it('pede a primeira página no tamanho da tela e entrega os itens', async () => {
    api.mockResolvedValue(pagina(['a', 'b'], 2))
    const { result } = montar()
    await waitFor(() => expect(result.current.itens).toEqual([{ id: 'a' }, { id: 'b' }]))
    expect(api).toHaveBeenCalledWith(`/lista?pagina=1&tamanho=${TAMANHO_DA_PAGINA}`, expect.objectContaining({ signal: expect.anything() }))
    expect(result.current.total).toBe(2)
  })

  it('a busca espera o usuário parar de digitar e volta à primeira página', async () => {
    api.mockResolvedValue(pagina(['a'], 120, 2))
    const { result } = montar()
    await waitFor(() => expect(result.current.itens).toBeDefined())
    act(() => result.current.irParaPagina(3))
    await waitFor(() => expect(api).toHaveBeenCalledWith(expect.stringContaining('pagina=3'), expect.anything()))

    vi.useFakeTimers()
    act(() => result.current.mudarTermo('pad'))
    expect(result.current.pagina).toBe(1)
    expect(result.current.esperandoBusca).toBe(true)
    expect(api.mock.calls.some(([caminho]) => caminho.includes('busca='))).toBe(false)
    await act(async () => { await vi.advanceTimersByTimeAsync(350) })
    vi.useRealTimers()
    await waitFor(() => expect(api).toHaveBeenCalledWith(`/lista?busca=pad&pagina=1&tamanho=${TAMANHO_DA_PAGINA}`, expect.anything()))
    expect(result.current.esperandoBusca).toBe(false)
  })

  it('resposta que chega atrasada não pisa na da página que está na tela', async () => {
    const primeira = adiada<ReturnType<typeof pagina>>()
    const segunda = adiada<ReturnType<typeof pagina>>()
    api.mockImplementation((caminho: string) => (caminho.includes('pagina=1') ? primeira.promessa : segunda.promessa) as Promise<never>)
    const { result } = montar()
    act(() => result.current.irParaPagina(2))
    await waitFor(() => expect(api).toHaveBeenCalledTimes(2))

    await act(async () => { segunda.resolver(pagina(['pagina-2'], 120)) })
    await waitFor(() => expect(result.current.itens).toEqual([{ id: 'pagina-2' }]))
    await act(async () => { primeira.resolver(pagina(['pagina-1'], 120)) })

    expect(result.current.pagina).toBe(2)
    expect(result.current.itens).toEqual([{ id: 'pagina-2' }])
  })

  it('mantém a página anterior na tela enquanto a próxima chega', async () => {
    const segunda = adiada<ReturnType<typeof pagina>>()
    api.mockImplementation((caminho: string) => (caminho.includes('pagina=2') ? segunda.promessa : Promise.resolve(pagina(['um'], 120))) as Promise<never>)
    const { result } = montar()
    await waitFor(() => expect(result.current.itens).toEqual([{ id: 'um' }]))
    act(() => result.current.irParaPagina(2))
    await waitFor(() => expect(result.current.consulta.isPlaceholderData).toBe(true))
    expect(result.current.itens).toEqual([{ id: 'um' }])
    await act(async () => { segunda.resolver(pagina(['dois'], 120)) })
    await waitFor(() => expect(result.current.itens).toEqual([{ id: 'dois' }]))
  })

  it('mudar um filtro da tela volta à primeira página', async () => {
    api.mockResolvedValue(pagina(['a'], 200, 2))
    const { result, rerender } = montar()
    await waitFor(() => expect(result.current.itens).toBeDefined())
    act(() => result.current.irParaPagina(4))
    expect(result.current.pagina).toBe(4)
    rerender({ filtros: { estado: 'Ativa' } })
    expect(result.current.pagina).toBe(1)
    await waitFor(() => expect(api).toHaveBeenCalledWith(`/lista?estado=Ativa&pagina=1&tamanho=${TAMANHO_DA_PAGINA}`, expect.anything()))
  })

  it('se uma gravação esvazia a última página, volta para a última que ainda tem item', async () => {
    api.mockImplementation((caminho: string) => Promise.resolve(caminho.includes('pagina=3') ? pagina([], 100) : pagina(['x'], 100, 3)) as Promise<never>)
    const { result } = montar()
    await waitFor(() => expect(result.current.itens).toBeDefined())
    act(() => result.current.irParaPagina(3))
    await waitFor(() => expect(result.current.pagina).toBe(2))
    expect(result.current.itens).toEqual([{ id: 'x' }])
  })

  it('o erro da API aparece na consulta e não deixa itens', async () => {
    api.mockRejectedValue(new Error('falhou'))
    const { result } = montar()
    await waitFor(() => expect(result.current.consulta.error).toBeTruthy())
    expect(result.current.itens).toBeUndefined()
  })
})
