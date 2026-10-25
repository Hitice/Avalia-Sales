import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, renderHook } from '@testing-library/react'
import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useSessao } from '@/acesso/sessao'
import { useAvisosAoVivo } from './avisosAoVivo'

const hub = vi.hoisted(() => ({
  aoAvisar: null as null | ((aviso: { tipo: string }) => void),
  aoReconectar: null as null | (() => void),
  aoFechar: null as null | (() => void),
  /** Quantos start() falham antes de um dar certo. */
  falhasNoStart: 0,
  inicios: 0,
  conexoes: [] as { stop: ReturnType<typeof vi.fn>; state: string }[],
}))

vi.mock('@microsoft/signalr', () => ({
  LogLevel: { Critical: 5 },
  HubConnectionState: { Disconnected: 'Disconnected' },
  HubConnectionBuilder: class {
    withUrl() { return this }
    withAutomaticReconnect() { return this }
    configureLogging() { return this }
    build() {
      const conexao = {
        state: 'Connected',
        stop: vi.fn(() => Promise.resolve()),
        start: () => {
          hub.inicios++
          return hub.falhasNoStart-- > 0 ? Promise.reject(new Error('servidor fora')) : Promise.resolve()
        },
        onreconnected: (f: () => void) => { hub.aoReconectar = f },
        onclose: (f: () => void) => { hub.aoFechar = f },
        on: (_nome: string, aoAvisar: (aviso: { tipo: string }) => void) => { hub.aoAvisar = aoAvisar },
      }
      hub.conexoes.push(conexao)
      return conexao
    }
  },
}))

const perfil = { id: '1', nome: 'Dono', email: 'dono@teste.com.br', papel: 'Dono' as const, revendedorId: 'r', revendedor: 'Revenda', emailConfirmado: true }

beforeEach(() => {
  hub.aoAvisar = null
  hub.aoReconectar = null
  hub.aoFechar = null
  hub.falhasNoStart = 0
  hub.inicios = 0
  hub.conexoes = []
  useSessao.getState().entrar({ token: 't', expiraEm: new Date(Date.now() + 900_000).toISOString(), perfil })
})

afterEach(() => {
  useSessao.getState().limpar()
  vi.useRealTimers()
})

function montar() {
  const cliente = new QueryClient()
  const invalidar = vi.spyOn(cliente, 'invalidateQueries')
  const tela = renderHook(() => useAvisosAoVivo(), { wrapper: ({ children }: { children: ReactNode }) => <QueryClientProvider client={cliente}>{children}</QueryClientProvider> })
  const avisar = (tipo: string) => {
    invalidar.mockClear()
    act(() => hub.aoAvisar?.({ tipo }))
    return invalidar.mock.calls.map(([filtro]) => String(filtro?.queryKey?.[0]))
  }
  return { ...tela, avisar, invalidar }
}

describe('avisos ao vivo', () => {
  it('pagamento confirmado refaz o resultado do mês e a lista de placas, além da venda e do razão', () => {
    const { avisar } = montar()
    expect(avisar('pagamento')).toEqual(expect.arrayContaining(['vendas', 'placa', 'placas', 'razao', 'comissoes', 'financeiro']))
  })

  it('venda nova refaz o vendido do mês e os clientes', () => {
    const { avisar } = montar()
    expect(avisar('venda')).toEqual(expect.arrayContaining(['vendas', 'placas', 'clientes', 'financeiro']))
  })

  it('repasse de comissão refaz o caixa', () => {
    const { avisar } = montar()
    expect(avisar('comissao')).toEqual(expect.arrayContaining(['comissoes', 'repasses', 'razao', 'financeiro']))
  })

  it('estoque atualizado refaz os lotes da revenda', () => {
    const { avisar } = montar()
    expect(avisar('estoque')).toEqual(expect.arrayContaining(['placas', 'pedidos', 'lotes']))
  })

  it('sair da sessão encerra a conexão', () => {
    const { rerender } = montar()
    act(() => useSessao.getState().limpar())
    rerender()
    expect(hub.conexoes[0].stop).toHaveBeenCalled()
  })

  it('se o servidor estava fora quando a tela abriu, tenta ligar de novo em vez de ficar sem avisos até recarregar', async () => {
    vi.useFakeTimers()
    hub.falhasNoStart = 1
    montar()
    await vi.advanceTimersByTimeAsync(0)
    expect(hub.inicios).toBe(1)

    await vi.advanceTimersByTimeAsync(10_000)

    expect(hub.inicios).toBe(2)
  })

  it('quando a reconexão automática desiste, liga de novo', async () => {
    vi.useFakeTimers()
    montar()
    await vi.advanceTimersByTimeAsync(0)
    expect(hub.inicios).toBe(1)

    hub.aoFechar?.()
    await vi.advanceTimersByTimeAsync(10_000)

    expect(hub.inicios).toBe(2)
  })

  it('ao reconectar, refaz tudo: os avisos do intervalo se perderam', async () => {
    const { avisar, invalidar } = montar()
    expect(avisar('chat')).toEqual(['chat'])
    invalidar.mockClear()

    act(() => hub.aoReconectar?.())

    expect(invalidar).toHaveBeenCalledWith()
  })

  it('sair da sessão no meio das tentativas para de tentar', async () => {
    vi.useFakeTimers()
    hub.falhasNoStart = 99
    const { rerender } = montar()
    await vi.advanceTimersByTimeAsync(0)

    act(() => useSessao.getState().limpar())
    rerender()
    await vi.advanceTimersByTimeAsync(120_000)

    expect(hub.inicios).toBe(1)
  })
})
