import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
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

/** Endereço curto com o botão de copiar, para mandar ao cliente pelo WhatsApp. */
export function CopiarLink({ rotulo, endereco }: { rotulo: string; endereco: string }) {
  const [copiado, setCopiado] = useState(false)
  return (
    <div className="grid gap-1">
      <p className="text-xs text-muted-foreground">{rotulo}</p>
      <div className="flex gap-2">
        <Input readOnly aria-label={rotulo} value={endereco.toLowerCase()} onFocus={(e) => e.target.select()} />
        <Button variant="outline" onClick={() => navigator.clipboard.writeText(endereco.toLowerCase()).then(() => setCopiado(true))}>{copiado ? 'Copiado' : 'Copiar'}</Button>
      </div>
    </div>
  )
}
