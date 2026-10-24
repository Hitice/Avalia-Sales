import { cn } from '@/lib/utils'

/**
 * O logo completo da plataforma: nas telas de entrada (antes do login não há
 * marca de revenda) e no topo do painel lateral do computador, para quem não
 * tem logo nem nome próprio. No cabeçalho do celular e do tablet vai só o gauge.
 */
export function MarcaAvalia({ className }: { className?: string }) {
  return <img src="/marca/avaliasales.svg" alt="Avalia Sales" width={192} height={79} className={cn('h-auto w-48', className)} />
}
