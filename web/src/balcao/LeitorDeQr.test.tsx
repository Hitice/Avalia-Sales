import { render, waitFor } from '@testing-library/react'
import { StrictMode } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { LeitorDeQr } from './LeitorDeQr'

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

/** Uma câmera falsa: cada getUserMedia devolve um fluxo com uma faixa que lembra se foi parada. */
function camerasFalsas() {
  const faixas: { stop: ReturnType<typeof vi.fn> }[] = []
  const pendentes: (() => void)[] = []
  const getUserMedia = vi.fn(() => new Promise<MediaStream>((resolver) => {
    const faixa = { stop: vi.fn() }
    faixas.push(faixa)
    pendentes.push(() => resolver({ getTracks: () => [faixa] } as unknown as MediaStream))
  }))
  vi.stubGlobal('navigator', { ...navigator, mediaDevices: { getUserMedia } })
  // O detector nativo e o play() do vídeo, que o jsdom não tem.
  vi.stubGlobal('BarcodeDetector', class { static getSupportedFormats = () => Promise.resolve(['qr_code']); detect = () => Promise.resolve([]) })
  vi.spyOn(HTMLMediaElement.prototype, 'play').mockResolvedValue(undefined)
  return { faixas, liberar: () => pendentes.splice(0).forEach((f) => f()) }
}

describe('LeitorDeQr', () => {
  it('fechar antes de a câmera responder libera a câmera quando ela chega', async () => {
    const { faixas, liberar } = camerasFalsas()
    const tela = render(<LeitorDeQr aoLer={() => undefined} aoFechar={() => undefined} />)
    await waitFor(() => expect(faixas).toHaveLength(1))

    tela.unmount()
    liberar()

    await waitFor(() => expect(faixas[0].stop).toHaveBeenCalled())
  })

  it('no StrictMode (montar, desmontar, montar), a câmera da primeira montagem não fica ligada', async () => {
    const { faixas, liberar } = camerasFalsas()
    render(<StrictMode><LeitorDeQr aoLer={() => undefined} aoFechar={() => undefined} /></StrictMode>)
    await waitFor(() => expect(faixas).toHaveLength(2))

    liberar()

    await waitFor(() => expect(faixas[0].stop).toHaveBeenCalled())
  })
})
