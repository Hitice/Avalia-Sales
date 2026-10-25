import { ChevronLeft, ChevronRight, Search } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'

/**
 * Pé da lista paginada pela API: "51 a 100 de 230" e Anterior/Próxima. Some
 * quando tudo cabe numa página. O botão fica a 44 px no celular, como os
 * outros. A faixa é anunciada ao leitor de tela quando a página muda.
 */
export function Paginacao({ pagina, tamanho, total, itensNaPagina, aoMudar, atualizando = false, nome = 'Paginação' }: {
  pagina: number
  tamanho: number
  total: number
  itensNaPagina: number
  aoMudar: (pagina: number) => void
  atualizando?: boolean
  nome?: string
}) {
  const paginas = Math.max(1, Math.ceil(total / tamanho))
  if (total === 0 || (paginas === 1 && pagina === 1)) return null
  const primeiro = (pagina - 1) * tamanho + 1
  const ultimo = Math.min(primeiro + Math.max(itensNaPagina, 1) - 1, total)

  return (
    <nav aria-label={nome} aria-busy={atualizando} className="flex flex-wrap items-center justify-between gap-x-4 gap-y-2 pt-3 text-sm md:px-5 md:pb-1">
      <p className="text-muted-foreground tabular-nums" aria-live="polite">{primeiro} a {ultimo} de {total}</p>
      <div className="flex items-center gap-2">
        <Button type="button" variant="outline" disabled={pagina <= 1} onClick={() => aoMudar(pagina - 1)}>
          <ChevronLeft aria-hidden />Anterior
        </Button>
        <span className="min-w-24 text-center text-muted-foreground tabular-nums">Página {pagina} de {paginas}</span>
        <Button type="button" variant="outline" disabled={pagina >= paginas} onClick={() => aoMudar(pagina + 1)}>
          Próxima<ChevronRight aria-hidden />
        </Button>
      </div>
    </nav>
  )
}

/** O campo de busca de uma lista paginada: o texto vai à API, e não filtra só a página aberta. */
export function BuscaDaLista({ valor, aoMudar, rotulo = 'Buscar na lista', placeholder = 'Buscar' }: {
  valor: string
  aoMudar: (texto: string) => void
  rotulo?: string
  placeholder?: string
}) {
  return (
    <div className="relative max-w-xs flex-1">
      <Search aria-hidden className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground" />
      <Input type="search" value={valor} onChange={(e) => aoMudar(e.target.value)} placeholder={placeholder} aria-label={rotulo} className="pl-8" />
    </div>
  )
}
