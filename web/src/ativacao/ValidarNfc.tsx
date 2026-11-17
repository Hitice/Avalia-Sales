import { useEffect, useRef, useState } from 'react'
import { Button } from '@/components/ui/button'
import { lerNfc, mensagemDoNfc, mesmoEndereco, nfcDisponivel } from '@/balcao/nfc'

/**
 * Confere a tag depois de gravada: no Android, o Chrome lê a tag e compara com o endereço da placa. No iPhone o próprio
 * aparelho abre o endereço da tag ao encostar, e isso já é a conferência.
 */
export function ValidarNfc({ endereco }: { endereco: string }) {
  const [situacao, setSituacao] = useState<'parado' | 'lendo' | 'certo' | 'errado'>('parado')
  const [erro, setErro] = useState<string | null>(null)
  const controle = useRef<AbortController | null>(null)
  useEffect(() => () => controle.current?.abort(), [])

  if (!nfcDisponivel()) {
    return <p className="text-xs text-muted-foreground">Para conferir, encoste o celular na placa: a avaliação ou o cardápio abre na hora.</p>
  }

  async function validar() {
    setErro(null)
    setSituacao('lendo')
    const atual = new AbortController()
    controle.current = atual
    try {
      const lido = await lerNfc(atual.signal)
      atual.abort()
      setSituacao(mesmoEndereco(lido, endereco) ? 'certo' : 'errado')
    } catch (e) {
      if (atual.signal.aborted) return
      setErro(mensagemDoNfc(e))
      setSituacao('parado')
    }
  }

  return (
    <div className="grid gap-1">
      <Button variant="outline" disabled={situacao === 'lendo'} onClick={validar}>
        {situacao === 'lendo' ? 'Encoste na placa...' : 'Conferir NFC'}
      </Button>
      {situacao === 'certo' && <p role="status" className="text-xs text-sucesso">NFC conferido.</p>}
      {situacao === 'errado' && <p role="alert" className="text-xs text-destructive">A tag tem outro endereço. Grave de novo.</p>}
      {erro && <p role="alert" className="text-xs text-destructive">{erro}</p>}
    </div>
  )
}
