import { useEffect, useState } from 'react'
import Cropper, { type Area as AreaDoCropper } from 'react-easy-crop'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import type { Area } from './reduzirLogo'

/** O cabeçalho mostra o logo com até 4 de largura para 1 de altura: horizontal em 3:1 cabe com folga. */
const FORMATOS = { Horizontal: 3, Quadrado: 1 } as const
type Formato = keyof typeof FORMATOS

/**
 * A caixa de recorte do logo: arrasta para enquadrar, zoom para aproximar ou
 * afastar. Afastar abaixo de 100% deixa sobra transparente, para logo largo
 * caber inteiro. O recorte sai em pixels da imagem original.
 */
export function RecorteDoLogo({ arquivo, aoCancelar, aoConfirmar, enviando }: {
  arquivo: File | null
  aoCancelar: () => void
  aoConfirmar: (area: Area) => void
  enviando: boolean
}) {
  const [endereco, setEndereco] = useState<string | null>(null)
  const [formato, setFormato] = useState<Formato>('Horizontal')
  const [posicao, setPosicao] = useState({ x: 0, y: 0 })
  const [zoom, setZoom] = useState(1)
  const [area, setArea] = useState<AreaDoCropper | null>(null)
  const [proporcao, setProporcao] = useState<number | null>(null)

  /** Começa com a imagem inteira dentro da moldura: logo mais largo que o formato ganha zoom menor que 100%. */
  function enquadrar(formatoEscolhido: Formato, proporcaoDaImagem: number | null) {
    const moldura = FORMATOS[formatoEscolhido]
    setPosicao({ x: 0, y: 0 })
    setZoom(proporcaoDaImagem ? Math.min(1, proporcaoDaImagem / moldura, moldura / proporcaoDaImagem) : 1)
  }

  useEffect(() => {
    if (!arquivo) return
    const url = URL.createObjectURL(arquivo)
    setEndereco(url)
    setPosicao({ x: 0, y: 0 })
    setZoom(1)
    return () => URL.revokeObjectURL(url)
  }, [arquivo])

  return (
    <Dialog open={!!arquivo} onOpenChange={(aberto) => !aberto && !enviando && aoCancelar()}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Recortar logo</DialogTitle>
          <DialogDescription>Arraste para enquadrar e use o zoom.</DialogDescription>
        </DialogHeader>
        <Tabs value={formato} onValueChange={(f) => { setFormato(f as Formato); enquadrar(f as Formato, proporcao) }}>
          <TabsList className="w-full">
            {Object.keys(FORMATOS).map((f) => <TabsTrigger key={f} value={f}>{f}</TabsTrigger>)}
          </TabsList>
        </Tabs>
        <div className="xadrez relative h-64 overflow-hidden rounded-lg sm:h-72">
          {endereco && (
            <Cropper
              image={endereco}
              crop={posicao}
              zoom={zoom}
              minZoom={0.5}
              maxZoom={4}
              aspect={FORMATOS[formato]}
              restrictPosition={false}
              objectFit="contain"
              onCropChange={setPosicao}
              onZoomChange={setZoom}
              onCropComplete={(_, pixels) => setArea(pixels)}
              onMediaLoaded={({ naturalWidth, naturalHeight }) => {
                const p = naturalWidth / naturalHeight
                setProporcao(p)
                enquadrar(formato, p)
              }}
            />
          )}
        </div>
        <label className="grid gap-1 text-sm">
          <span className="flex justify-between"><span>Zoom</span><span className="tabular-nums text-muted-foreground">{Math.round(zoom * 100)}%</span></span>
          <input type="range" min={0.5} max={4} step={0.01} value={zoom} onChange={(e) => setZoom(Number(e.target.value))} className="accent-primary" />
        </label>
        <DialogFooter>
          <Button variant="ghost" onClick={aoCancelar} disabled={enviando}>Cancelar</Button>
          <Button onClick={() => area && aoConfirmar(area)} disabled={!area || enviando}>{enviando ? 'Enviando...' : 'Usar este recorte'}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
