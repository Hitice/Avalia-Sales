import type { ReactNode } from 'react'
import { Card } from '@/components/ui/card'
import { cn } from '@/lib/utils'

/**
 * Um número do painel (DESIGN.md): rótulo de uma linha em cima, valor em 24 px
 * embaixo, alinhados à esquerda e com a mesma altura em toda a grade. Destaque
 * é o número que pede ação (comissão a receber): o valor vai no verde de
 * sucesso, sem anel nem fundo.
 */
export function Indicador({ rotulo, valor, destaque = false }: { rotulo: string; valor: ReactNode; destaque?: boolean }) {
  return (
    <Card className="gap-1 px-4 py-3.5 md:px-5 md:py-4">
      <p className="truncate text-sm text-muted-foreground" title={rotulo}>{rotulo}</p>
      <p className={cn('text-xl font-medium tracking-tight tabular-nums sm:text-2xl', destaque && 'text-sucesso')}>{valor}</p>
    </Card>
  )
}

/** A grade dos indicadores: duas colunas no celular, até quatro no computador. */
export function Indicadores({ children, colunas = 4 }: { children: ReactNode; colunas?: 3 | 4 }) {
  return <div className={cn('grid grid-cols-2 gap-3 md:gap-4', colunas === 4 ? 'lg:grid-cols-4' : 'lg:grid-cols-3')}>{children}</div>
}
