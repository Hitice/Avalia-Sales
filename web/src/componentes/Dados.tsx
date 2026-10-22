import type { ReactNode } from 'react'

/**
 * Pares rótulo e valor em linhas: rótulo à esquerda, valor à direita, um por
 * linha e separados por fio. O jeito de ler dados de um registro no celular
 * sem amontoar (ficha da placa, detalhe de venda).
 */
export function Dados({ itens }: { itens: [rotulo: string, valor: ReactNode][] }) {
  return (
    <dl className="divide-y text-sm">
      {itens.map(([rotulo, valor]) => (
        <div key={rotulo} className="flex items-baseline justify-between gap-4 py-2 first:pt-0 last:pb-0">
          <dt className="shrink-0 text-muted-foreground">{rotulo}</dt>
          <dd className="min-w-0 text-right font-medium break-words">{valor}</dd>
        </div>
      ))}
    </dl>
  )
}
