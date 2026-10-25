import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { CopiarLink, GravarNfc } from './BotoesDeNfc'

afterEach(() => {
  Reflect.deleteProperty(window, 'NDEFReader')
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

describe('GravarNfc', () => {
  it('sair da tela no meio da gravação cancela a gravação pendente', async () => {
    let sinal: AbortSignal | undefined
    window.NDEFReader = class {
      write(_mensagem: unknown, opcoes?: { signal?: AbortSignal }) {
        sinal = opcoes?.signal
        return new Promise<void>(() => undefined)
      }
    }
    const tela = render(<GravarNfc endereco="https://avaliaone.com.br/q/K7M2PX" />)

    fireEvent.click(screen.getByRole('button', { name: 'Gravar NFC da placa' }))
    await waitFor(() => expect(sinal).toBeDefined())
    expect(sinal?.aborted).toBe(false)

    tela.unmount()

    expect(sinal?.aborted).toBe(true)
  })
})

describe('CopiarLink', () => {
  it('sem permissão para a área de transferência, avisa em vez de fingir que copiou', async () => {
    vi.stubGlobal('navigator', { ...navigator, clipboard: { writeText: () => Promise.reject(new DOMException('negado', 'NotAllowedError')) } })
    render(<CopiarLink rotulo="Link de avaliação" endereco="https://avaliaone.com.br/L/ABC" />)

    fireEvent.click(screen.getByRole('button', { name: 'Copiar' }))

    expect(await screen.findByText(/Não deu para copiar/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Copiado' })).toBeNull()
  })
})
