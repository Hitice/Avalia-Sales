import { ImageOff, Plus, QrCode } from 'lucide-react'
import { useState } from 'react'
import type { ItemDoCatalogo } from '@/api/tipos'
import { usaPlaca } from '@/catalogo/produtos'
import { Badge } from '@/components/ui/badge'
import { formatarCentavos } from '@/dinheiro/dinheiro'
import { cn } from '@/lib/utils'
import { quantidadeMinima } from './itens'

/**
 * O catálogo na Nova venda, uma linha por item para o polegar: foto, nome e
 * preço. Placa abre a leitura do QR; serviço entra na venda com o pedido
 * mínimo. O número à direita é quanto desse item já está na venda.
 */
export function ListaDeItens({ itens, naVenda, aoEscolher }: {
  itens: ItemDoCatalogo[]
  /** Quantidade de cada item já na venda, pelo id. */
  naVenda: Record<string, number>
  aoEscolher: (item: ItemDoCatalogo) => void
}) {
  if (itens.length === 0) return <p className="text-sm text-muted-foreground">O catálogo está vazio.</p>

  return (
    <ul aria-label="Itens do catálogo" className="-mx-2 grid gap-0.5">
      {itens.map((item) => {
        const placa = usaPlaca(item)
        const minimo = quantidadeMinima(item)
        const quantos = naVenda[item.id] ?? 0
        return (
          <li key={item.id}>
            <button
              type="button"
              onClick={() => aoEscolher(item)}
              className={cn('flex min-h-16 w-full items-center gap-3 rounded-md p-2 text-left transition-colors duration-150 hover:bg-cinza-50 focus-visible:bg-cinza-50 active:bg-muted', quantos > 0 && 'bg-accent hover:bg-accent')}
            >
              <Miniatura item={item} />
              <span className="grid min-w-0 flex-1">
                <span className="truncate font-medium">{item.nome}</span>
                <span className="text-sm text-muted-foreground tabular-nums">
                  {formatarCentavos(item.precoCents)}
                  {placa ? ' por placa' : minimo > 1 ? `, mínimo ${minimo}` : ''}
                </span>
              </span>
              {quantos > 0
                ? <Badge aria-label={`${quantos} na venda`}>{quantos}</Badge>
                : placa
                  ? <QrCode aria-hidden className="size-5 shrink-0 text-muted-foreground" />
                  : <Plus aria-hidden className="size-5 shrink-0 text-muted-foreground" />}
            </button>
          </li>
        )
      })}
    </ul>
  )
}

function Miniatura({ item }: { item: ItemDoCatalogo }) {
  const [falhou, setFalhou] = useState(false)
  if (!item.fotoUrl || falhou) {
    return <span aria-hidden className="flex size-12 shrink-0 items-center justify-center rounded-md bg-muted text-muted-foreground"><ImageOff className="size-5" /></span>
  }
  return <img src={item.fotoUrl} alt="" loading="lazy" className="size-12 shrink-0 rounded-md bg-muted object-cover" onError={() => setFalhou(true)} />
}
