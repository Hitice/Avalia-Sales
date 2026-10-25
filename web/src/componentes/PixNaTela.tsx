import QRCode from 'qrcode'
import { useEffect, useState } from 'react'
import { Button } from '@/components/ui/button'
import { copiar } from '@/lib/area'

/** O QR e o copia e cola do Pix, para o cliente pagar ali no balcão. */
export function PixNaTela({ copiaECola, imagemDoGateway }: { copiaECola: string; imagemDoGateway: string | null }) {
  const [imagem, setImagem] = useState<string | null>(null)
  const [copiado, setCopiado] = useState(false)
  const [recusado, setRecusado] = useState(false)

  useEffect(() => {
    if (!imagemDoGateway) {
      QRCode.toDataURL(copiaECola, { errorCorrectionLevel: 'M', margin: 1, width: 320 }).then(setImagem, () => setImagem(null))
    }
  }, [copiaECola, imagemDoGateway])

  return (
    <div className="grid gap-3">
      <img className="mx-auto w-64 rounded-lg border bg-card p-2" alt="QR do Pix para o cliente pagar" src={imagemDoGateway ?? imagem ?? undefined} />
      <Button
        variant="outline"
        onClick={async () => {
          const copiou = await copiar(copiaECola)
          setCopiado(copiou)
          setRecusado(!copiou)
        }}
      >
        {copiado ? 'Copiado' : 'Copiar Pix copia e cola'}
      </Button>
      {recusado && <p role="alert" className="text-center text-sm text-destructive">Não deu para copiar. Peça ao cliente para ler o QR.</p>}
    </div>
  )
}
