import { Nfc } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { gravarNfc, mensagemDoNfc, nfcDisponivel } from './nfc'

/** O NFC Tools nas lojas: no iPhone nenhum navegador grava NFC, e é o app que o Avalia já usa no balcão. */
const NFC_TOOLS = {
  ios: 'https://apps.apple.com/app/id1252962749',
  android: 'https://play.google.com/store/apps/details?id=com.wakdev.wdnfc',
}

const ehIphone = () => /iPhone|iPad|iPod/.test(navigator.userAgent) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1)

/**
 * Sem Web NFC (iPhone, ou Android fora do Chrome), copia o endereço e abre o
 * NFC Tools. Ele não tem link documentado para a tela de gravação: a loja
 * mostra "Abrir" quando o app já está instalado.
 */
function GravarNoNfcTools({ endereco }: { endereco: string }) {
  const [aberto, setAberto] = useState(false)
  async function abrir() {
    await navigator.clipboard.writeText(endereco).catch(() => undefined)
    setAberto(true)
    window.open(ehIphone() ? NFC_TOOLS.ios : NFC_TOOLS.android, '_blank', 'noopener')
  }

  return (
    <div className="grid gap-2">
      <Button variant="outline" onClick={abrir}>
        <Nfc aria-hidden className="size-5" />
        Gravar com o NFC Tools
      </Button>
      {aberto && (
        <ol className="list-decimal pl-5 text-xs text-muted-foreground">
          <li>Endereço copiado.</li>
          <li>No NFC Tools: Gravar, Adicionar registro, URL.</li>
          <li>Cole, confirme e toque em Gravar.</li>
          <li>Encoste o celular na placa.</li>
        </ol>
      )}
    </div>
  )
}

/** Grava o endereço da placa numa tag NFC em branco, para a placa abrir também por aproximação. */
export function GravarNfc({ endereco }: { endereco: string }) {
  const [situacao, setSituacao] = useState<'parado' | 'gravando' | 'gravado'>('parado')
  const [erro, setErro] = useState<string | null>(null)
  const controle = useRef<AbortController | null>(null)
  // Sair da tela no meio da gravação solta o NFC do aparelho: a gravação pendente é cancelada.
  useEffect(() => () => controle.current?.abort(), [])
  if (!nfcDisponivel()) return <GravarNoNfcTools endereco={endereco} />

  async function gravar() {
    setErro(null)
    setSituacao('gravando')
    const atual = new AbortController()
    controle.current = atual
    try {
      await gravarNfc(endereco, atual.signal)
      setSituacao('gravado')
    } catch (e) {
      if (atual.signal.aborted) return
      setErro(mensagemDoNfc(e))
      setSituacao('parado')
    }
  }

  return (
    <div className="grid gap-1">
      <Button variant="outline" disabled={situacao === 'gravando'} onClick={gravar}>
        <Nfc aria-hidden className="size-5" />
        {situacao === 'gravando' ? 'Encoste na tag...' : situacao === 'gravado' ? 'NFC gravado' : 'Gravar NFC da placa'}
      </Button>
      {erro && <p role="alert" className="text-xs text-destructive">{erro}</p>}
    </div>
  )
}

/** Endereço curto com o botão de copiar, para mandar ao cliente pelo WhatsApp. */
export function CopiarLink({ rotulo, endereco }: { rotulo: string; endereco: string }) {
  const [copiado, setCopiado] = useState(false)
  const [recusado, setRecusado] = useState(false)
  const copiar = () => navigator.clipboard.writeText(endereco.toLowerCase()).then(() => { setCopiado(true); setRecusado(false) }, () => setRecusado(true))
  return (
    <div className="grid gap-1">
      <p className="text-xs text-muted-foreground">{rotulo}</p>
      <div className="flex gap-2">
        <Input readOnly aria-label={rotulo} value={endereco.toLowerCase()} onFocus={(e) => e.target.select()} />
        <Button variant="outline" onClick={copiar}>{copiado ? 'Copiado' : 'Copiar'}</Button>
      </div>
      {recusado && <p role="alert" className="text-xs text-destructive">Não deu para copiar. Toque no endereço, selecione e copie.</p>}
    </div>
  )
}
