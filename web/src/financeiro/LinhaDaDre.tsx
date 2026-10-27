import { formatarCentavos } from '@/dinheiro/dinheiro'
import { cn } from '@/lib/utils'

/** Uma linha da DRE: rótulo e valor; a saída vem com o sinal de menos, e o recuo marca o detalhe. */
export function LinhaDaDre({ rotulo, cents, saida = false, detalhe = false, total = false }: { rotulo: string; cents: number; saida?: boolean; detalhe?: boolean; total?: boolean }) {
  return (
    <div className={cn('flex items-baseline justify-between gap-3 py-1', detalhe && 'pl-4 text-xs text-muted-foreground', total && 'mt-1 border-t pt-2 font-semibold')}>
      <dt>{rotulo}</dt>
      <dd className={cn('tabular-nums', total && cents < 0 && 'text-destructive')}>{formatarCentavos(saida && cents !== 0 ? -cents : cents)}</dd>
    </div>
  )
}
