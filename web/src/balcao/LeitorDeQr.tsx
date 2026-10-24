import { useEffect, useRef, useState } from 'react'
import { Button } from '@/components/ui/button'
import { extrairCodigo } from './codigo'

type Detector = { detect: (fonte: HTMLVideoElement) => Promise<{ rawValue: string }[]> }

/**
 * O detector nativo do navegador (Chrome do Android) e, sem ele, o polyfill
 * do mesmo padrão sobre o ZXing em WebAssembly, carregado só quando precisa e
 * servido pelo próprio app, sem CDN.
 */
async function criarDetector(): Promise<Detector> {
  const nativo = (globalThis as { BarcodeDetector?: { new (o: { formats: string[] }): Detector; getSupportedFormats: () => Promise<string[]> } }).BarcodeDetector
  if (nativo && (await nativo.getSupportedFormats()).includes('qr_code')) {
    return new nativo({ formats: ['qr_code'] })
  }

  const [{ BarcodeDetector, prepareZXingModule }, { default: wasm }] = await Promise.all([
    import('barcode-detector/ponyfill'),
    import('zxing-wasm/reader/zxing_reader.wasm?url'),
  ])
  prepareZXingModule({ overrides: { locateFile: (caminho: string, prefixo: string) => (caminho.endsWith('.wasm') ? wasm : prefixo + caminho) } })
  return new BarcodeDetector({ formats: ['qr_code'] })
}

function mensagemDaCamera(erro: unknown): string {
  if (erro instanceof DOMException && erro.name === 'NotAllowedError') return 'Libere a câmera para este site e tente de novo.'
  if (erro instanceof DOMException && erro.name === 'NotFoundError') return 'Este aparelho não tem câmera disponível.'
  return 'Não deu para abrir a câmera.'
}

/** A câmera traseira lendo até achar o QR de uma placa. */
export function LeitorDeQr({ aoLer, aoFechar }: { aoLer: (codigo: string) => void; aoFechar: () => void }) {
  const video = useRef<HTMLVideoElement>(null)
  const [erro, setErro] = useState<string | null>(null)
  const [outroQr, setOutroQr] = useState(false)

  useEffect(() => {
    let parado = false
    let fluxo: MediaStream | null = null

    async function ler() {
      try {
        fluxo = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'environment' }, audio: false })
        if (parado || !video.current) return
        video.current.srcObject = fluxo
        await video.current.play()
        const detector = await criarDetector()
        while (!parado) {
          const achados = await detector.detect(video.current).catch(() => [])
          const codigo = achados.map((a) => extrairCodigo(a.rawValue)).find(Boolean)
          if (codigo) {
            aoLer(codigo)
            return
          }

          setOutroQr(achados.length > 0)
          await new Promise((r) => setTimeout(r, 200))
        }
      } catch (e) {
        setErro(mensagemDaCamera(e))
      }
    }

    void ler()
    return () => {
      parado = true
      fluxo?.getTracks().forEach((t) => t.stop())
    }
  }, [aoLer])

  return (
    <div className="grid gap-3">
      {erro
        ? <p role="alert" className="text-sm text-destructive">{erro}</p>
        : <video ref={video} className="aspect-square w-full rounded-lg bg-foreground object-cover" muted playsInline aria-label="Câmera lendo o QR" />}
      {outroQr && <p className="text-sm text-muted-foreground">Este QR não é de uma placa.</p>}
      <Button variant="outline" onClick={aoFechar}>Digitar o código</Button>
    </div>
  )
}
