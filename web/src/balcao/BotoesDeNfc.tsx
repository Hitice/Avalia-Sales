import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { gravarNfc, mensagemDoNfc, nfcDisponivel } from './nfc'

/** Grava o endereço da placa numa tag NFC em branco, para a placa abrir também por aproximação. */
export function GravarNfc({ endereco }: { endereco: string }) {
  const [situacao, setSituacao] = useState<'parado' | 'gravando' | 'gravado'>('parado')
  const [erro, setErro] = useState<string | null>(null)
  if (!nfcDisponivel()) return <Button variant="outline" disabled>Gravar NFC: só no Chrome do Android</Button>

  async function gravar() {
    setErro(null)
    setSituacao('gravando')
    const controle = new AbortController()
    try {
      await gravarNfc(endereco, controle.signal)
      setSituacao('gravado')
    } catch (e) {
      setErro(mensagemDoNfc(e))
      setSituacao('parado')
    }
  }

  return (
    <div className="grid gap-1">
      <Button variant="outline" disabled={situacao === 'gravando'} onClick={gravar}>
        {situacao === 'gravando' ? 'Encoste na tag...' : situacao === 'gravado' ? 'NFC gravado' : 'Gravar NFC da placa'}
      </Button>
      {erro && <p role="alert" className="text-xs text-destructive">{erro}</p>}
    </div>
  )
}
