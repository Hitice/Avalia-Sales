import QRCode from 'qrcode'
import { useEffect, useState } from 'react'
import { Button } from '@/components/ui/button'

/** O QR e o copia e cola do Pix, para o cliente pagar ali no balcão. */
export function PixNaTela({ copiaECola, imagemDoGateway }: { copiaECola: string; imagemDoGateway: string | null }) {
  const [imagem, setImagem] = useState<string | null>(null)
  const [copiado, setCopiado] = useState(false)

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
          await navigator.clipboard.writeText(copiaECola)
          setCopiado(true)
        }}
      >
        {copiado ? 'Copiado' : 'Copiar Pix copia e cola'}
      </Button>
    </div>
  )
}
