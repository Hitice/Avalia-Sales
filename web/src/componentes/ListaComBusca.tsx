import type { ReactNode } from 'react'
import { useState } from 'react'
import { Input } from '@/components/ui/input'

const normalizar = (texto: string) => texto.normalize('NFD').replace(/\p{Diacritic}/gu, '').toLowerCase()

/** A partir de quantos itens a lista ganha a busca, como a tabela. */
const ITENS_PARA_BUSCA = 7

/**
 * Lista de cartões que rola dentro dela e, a partir de sete itens, ganha a
 * busca pelo texto de cada um (sem acento). O mesmo comportamento da tabela.
 */
export function ListaComBusca<T>({ itens, texto, chave, children }: { itens: T[]; texto: (item: T) => string; chave: (item: T) => string; children: (item: T) => ReactNode }) {
  const [termo, setTermo] = useState('')
  const busca = normalizar(termo.trim())
  const achados = busca ? itens.filter((i) => normalizar(texto(i)).includes(busca)) : itens

  return (
    <div className="grid content-start gap-3">
      {(itens.length >= ITENS_PARA_BUSCA || termo) && (
        <div className="flex items-center gap-3">
          <Input type="search" value={termo} onChange={(e) => setTermo(e.target.value)} placeholder="Buscar na lista" aria-label="Buscar na lista" />
          {busca && <p className="shrink-0 text-xs text-muted-foreground" aria-live="polite">{achados.length} de {itens.length}</p>}
        </div>
      )}
      <div className="rolagem-da-tabela grid content-start gap-3" tabIndex={0} role="region" aria-label="Lista">
        {achados.map((i) => <div key={chave(i)}>{children(i)}</div>)}
        {busca && achados.length === 0 && <p className="text-sm text-muted-foreground">Nada na lista com "{termo.trim()}".</p>}
      </div>
    </div>
  )
}
