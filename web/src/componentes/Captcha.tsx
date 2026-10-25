import { useEffect, useRef, useState } from 'react'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { CHAVE_DO_CAPTCHA } from '@/lib/captcha'

const ENDERECO_DO_SCRIPT = 'https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit'

type OpcoesDoWidget = {
  sitekey: string
  theme: 'auto'
  language: 'pt-br'
  callback: (token: string) => void
  'expired-callback': () => void
  'error-callback': () => void
}

type Turnstile = {
  render: (elemento: HTMLElement, opcoes: OpcoesDoWidget) => string
  reset: (id: string) => void
  remove: (id: string) => void
}

declare global {
  interface Window {
    turnstile?: Turnstile
  }
}

let carregamento: Promise<Turnstile> | null = null

/** Carrega o script da Cloudflare uma vez, só quando o widget aparece pela primeira vez. */
function carregarScript(): Promise<Turnstile> {
  if (window.turnstile) return Promise.resolve(window.turnstile)
  carregamento ??= new Promise<Turnstile>((resolver, rejeitar) => {
    const script = document.createElement('script')
    script.src = ENDERECO_DO_SCRIPT
    script.async = true
    script.onload = () => (window.turnstile ? resolver(window.turnstile) : rejeitar(new Error('Turnstile ausente')))
    script.onerror = () => {
      carregamento = null
      script.remove()
      rejeitar(new Error('Turnstile não carregou'))
    }
    document.head.append(script)
  })
  return carregamento
}

type Props = {
  chave?: string
  /** Token do desafio resolvido; nulo quando vence, falha ou é reiniciado. */
  aoMudar: (token: string | null) => void
  /** Muda para pedir um desafio novo: o token vale uma vez só. */
  reinicio?: number
}

/**
 * Widget do Cloudflare Turnstile. O token vai no corpo do cadastro e o servidor
 * o confere. O script vem de challenges.cloudflare.com (liberado na CSP do Nginx)
 * e só é pedido se existir chave.
 */
export function Captcha({ chave = CHAVE_DO_CAPTCHA, aoMudar, reinicio = 0 }: Props) {
  const caixa = useRef<HTMLDivElement>(null)
  const idDoWidget = useRef<string | null>(null)
  const aoMudarAtual = useRef(aoMudar)
  const [falhou, setFalhou] = useState(false)

  useEffect(() => {
    aoMudarAtual.current = aoMudar
  })

  useEffect(() => {
    let ativo = true
    carregarScript().then((turnstile) => {
      if (!ativo || !caixa.current) return
      idDoWidget.current = turnstile.render(caixa.current, {
        sitekey: chave,
        theme: 'auto',
        language: 'pt-br',
        callback: (token) => aoMudarAtual.current(token),
        'expired-callback': () => aoMudarAtual.current(null),
        'error-callback': () => {
          aoMudarAtual.current(null)
          setFalhou(true)
        },
      })
    }).catch(() => ativo && setFalhou(true))
    return () => {
      ativo = false
      if (idDoWidget.current) window.turnstile?.remove(idDoWidget.current)
      idDoWidget.current = null
    }
  }, [chave])

  useEffect(() => {
    if (reinicio > 0 && idDoWidget.current) window.turnstile?.reset(idDoWidget.current)
  }, [reinicio])

  return (
    <div className="grid gap-2">
      <div ref={caixa} aria-label="Verificação de segurança" className="min-h-16" />
      {falhou && (
        <Alert variant="destructive">
          <AlertDescription>Não foi possível carregar a verificação de segurança. Recarregue a página e tente de novo.</AlertDescription>
        </Alert>
      )}
    </div>
  )
}
