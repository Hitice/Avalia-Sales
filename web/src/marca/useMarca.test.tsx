import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { renderHook, waitFor } from '@testing-library/react'
import type { ReactNode } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { useSessao } from '@/acesso/sessao'
import { type Marca, useLogo, useMarca } from './marca'

const marca: Marca = {
  nomeExibido: 'Padaria', corPrimaria: '#1D4ED8', textoSobrePrimaria: '#FFFFFF', corDestaque: '#D1006F', textoSobreDestaque: '#FFFFFF',
  temLogo: true, versao: 3, cabecalhoNaCor: false,
}

function comConsultas() {
  const cliente = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return ({ children }: { children: ReactNode }) => <QueryClientProvider client={cliente}>{children}</QueryClientProvider>
}

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
  useSessao.getState().limpar()
  document.documentElement.removeAttribute('style')
})

describe('marca da revenda na tela', () => {
  it('o painel da marca que fecha não apaga as cores do app; o último a sair devolve as da plataforma', async () => {
    useSessao.getState().entrar({
      token: 't', expiraEm: new Date(Date.now() + 900_000).toISOString(),
      perfil: { id: '1', nome: 'X', email: 'x@teste.com.br', papel: 'Dono', revendedorId: 'r', revendedor: 'R', emailConfirmado: true },
    })
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify(marca), { status: 200 })))
    const embrulho = comConsultas()
    const moldura = renderHook(useMarca, { wrapper: embrulho })
    const painel = renderHook(useMarca, { wrapper: embrulho })
    await waitFor(() => expect(document.documentElement.style.getPropertyValue('--primary')).toBe('#1D4ED8'))

    painel.unmount()
    expect(document.documentElement.style.getPropertyValue('--primary')).toBe('#1D4ED8')

    moldura.unmount()
    expect(document.documentElement.style.getPropertyValue('--primary')).toBe('')
  })

  it('o painel que fecha não libera o logo que a Moldura mostra; a versão nova libera a anterior', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response(new Blob(['png']), { status: 200 })))
    let n = 0
    vi.spyOn(URL, 'createObjectURL').mockImplementation(() => `blob:logo-${++n}`)
    const liberados: string[] = []
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation((u) => { liberados.push(u) })
    const embrulho = comConsultas()
    const moldura = renderHook(({ m }) => useLogo(m), { wrapper: embrulho, initialProps: { m: marca } })
    const painel = renderHook(() => useLogo(marca), { wrapper: embrulho })
    await waitFor(() => expect(moldura.result.current).not.toBeNull())
    const primeiro = moldura.result.current

    painel.unmount()
    expect(liberados).not.toContain(primeiro)

    moldura.rerender({ m: { ...marca, versao: 4 } })
    await waitFor(() => expect(moldura.result.current).not.toBe(primeiro))
    expect(liberados).toContain(primeiro)
  })
})
