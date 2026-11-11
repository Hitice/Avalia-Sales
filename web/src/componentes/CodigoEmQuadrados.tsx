import { useState } from 'react'
import { cn } from '@/lib/utils'

/**
 * O código de 6 dígitos em quadrados. Por baixo é um campo só, transparente
 * sobre os quadrados: colar, o preenchimento do celular (one-time-code) e o
 * leitor de tela funcionam como num campo comum. Completo, chama `aoCompletar`.
 */
export function CodigoEmQuadrados({ id, valor, aoMudar, aoCompletar, rotulo, invalido = false, desabilitado = false, digitos = 6 }: {
  id: string
  valor: string
  aoMudar: (valor: string) => void
  aoCompletar?: (valor: string) => void
  rotulo: string
  invalido?: boolean
  desabilitado?: boolean
  digitos?: number
}) {
  const [focado, setFocado] = useState(false)

  return (
    <div className="relative grid w-full max-w-xs grid-cols-6 gap-2">
      {Array.from({ length: digitos }, (_, i) => {
        const atual = focado && (i === valor.length || (valor.length === digitos && i === digitos - 1))
        return (
          <div key={i} aria-hidden
            className={cn('flex aspect-square items-center justify-center rounded-lg border border-input bg-background text-lg font-medium tabular-nums transition-colors',
              atual && 'border-ring ring-3 ring-ring/50',
              invalido && 'border-destructive ring-3 ring-destructive/20',
              desabilitado && 'opacity-50')}>
            {valor[i] ?? ''}
          </div>
        )
      })}
      <input id={id} aria-label={rotulo} aria-invalid={invalido} value={valor} disabled={desabilitado}
        inputMode="numeric" autoComplete="one-time-code" pattern="[0-9]*" autoFocus
        className="absolute inset-0 w-full cursor-text bg-transparent text-transparent caret-transparent outline-none selection:bg-transparent"
        onFocus={() => setFocado(true)} onBlur={() => setFocado(false)}
        onChange={(e) => {
          const limpo = e.target.value.replace(/\D/g, '').slice(0, digitos)
          aoMudar(limpo)
          if (limpo.length === digitos && limpo !== valor) aoCompletar?.(limpo)
        }} />
    </div>
  )
}
