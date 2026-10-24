import type { ReactNode } from 'react'
import { Card } from '@/components/ui/card'
import { cn } from '@/lib/utils'

/**
 * Um número do painel: rótulo de uma linha em cima, valor embaixo, alinhados
 * à esquerda e com a mesma altura em toda a grade. Destaque é o número que
 * pede ação (comissão a receber), com o anel na cor de sucesso.
 */
export function Indicador({ rotulo, valor, destaque = false }: { rotulo: string; valor: ReactNode; destaque?: boolean }) {
  return (
    <Card className={cn('gap-1 px-4 py-3.5', destaque && 'ring-2 ring-sucesso')}>
      <p className="truncate text-xs font-medium text-muted-foreground" title={rotulo}>{rotulo}</p>
      <p className="text-xl font-semibold tabular-nums sm:text-2xl">{valor}</p>
    </Card>
  )
}

/** A grade dos indicadores: duas colunas no celular, até quatro no computador. */
export function Indicadores({ children, colunas = 4 }: { children: ReactNode; colunas?: 3 | 4 }) {
  return <div className={cn('grid grid-cols-2 gap-3', colunas === 4 ? 'lg:grid-cols-4' : 'lg:grid-cols-3')}>{children}</div>
}
