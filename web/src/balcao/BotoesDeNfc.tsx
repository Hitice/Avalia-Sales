import { useEffect, useRef, useState } from 'react'
import { Button } from '@/components/ui/button'
import { extrairCodigo } from './codigo'
import { gravarNfc, lerNfc, mensagemDoNfc, nfcDisponivel } from './nfc'

/** Encostar o celular na placa para ler o código pela tag NFC. */
export function LerNfc({ aoLer }: { aoLer: (codigo: string) => void }) {
  const [lendo, setLendo] = useState(false)
  const [erro, setErro] = useState<string | null>(null)
  const parar = useRef<AbortController | null>(null)
  useEffect(() => () => parar.current?.abort(), [])

  async function ler() {
    setErro(null)
    setLendo(true)
    parar.current = new AbortController()
    try {
      const codigo = extrairCodigo(await lerNfc(parar.current.signal))
      if (codigo) aoLer(codigo)
      else setErro('A tag não tem o endereço de uma placa.')
    } catch (e) {
      setErro(mensagemDoNfc(e))
    } finally {
      setLendo(false)
      parar.current.abort()
    }
  }

  return (
    <div className="grid gap-1">
      <Button size="lg" variant="outline" onClick={lendo ? () => parar.current?.abort() : ler}>{lendo ? 'Encoste na placa...' : 'Ler NFC'}</Button>
      {erro && <p role="alert" className="text-xs text-destructive">{erro}</p>}
    </div>
  )
}

/** Grava o endereço da placa numa tag NFC em branco, para a placa abrir também por aproximação. */
export function GravarNfc({ endereco }: { endereco: string }) {
  const [situacao, setSituacao] = useState<'parado' | 'gravando' | 'gravado'>('parado')
  const [erro, setErro] = useState<string | null>(null)
  if (!nfcDisponivel()) return null

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
