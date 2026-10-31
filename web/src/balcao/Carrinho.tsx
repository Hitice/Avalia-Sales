import { Minus, Pencil, Plus, Trash2 } from 'lucide-react'
import { useEffect, useState } from 'react'
import { personalizada, usaPlaca } from '@/catalogo/produtos'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { formatarCentavos, paraCentavos } from '@/dinheiro/dinheiro'
import { type LinhaDaVenda, quantidadeMinima, total, totalDaLinha } from './itens'

/**
 * As linhas da venda: a placa com o destino, o serviço com a quantidade
 * (nunca abaixo do pedido mínimo) e o total. O valor de uma linha pode ser
 * ajustado, como o "Valor cobrado" de antes, só que por item.
 */
export function Carrinho({ linhas, aoRemover, aoMudarQuantidade, aoMudarValor }: {
  linhas: LinhaDaVenda[]
  aoRemover: (id: string) => void
  aoMudarQuantidade: (id: string, quantidade: number) => void
  aoMudarValor: (id: string, valorCents: number | null) => void
}) {
  if (linhas.length === 0) return <p className="text-sm text-muted-foreground">Nenhum item ainda.</p>

  return (
    <div className="grid gap-3">
      <ul aria-label="Itens da venda" className="grid divide-y">
        {linhas.map((l) => (
          <Linha key={l.id} linha={l} aoRemover={() => aoRemover(l.id)} aoMudarQuantidade={(q) => aoMudarQuantidade(l.id, q)} aoMudarValor={(v) => aoMudarValor(l.id, v)} />
        ))}
      </ul>
      <p className="flex items-center justify-between border-t pt-3 font-semibold">
        <span>Total</span>
        <span className="tabular-nums" data-testid="total-da-venda">{formatarCentavos(total(linhas))}</span>
      </p>
    </div>
  )
}

function Linha({ linha, aoRemover, aoMudarQuantidade, aoMudarValor }: {
  linha: LinhaDaVenda
  aoRemover: () => void
  aoMudarQuantidade: (quantidade: number) => void
  aoMudarValor: (valorCents: number | null) => void
}) {
  // A personalizada também é uma por linha, com os destinos dela.
  const placa = usaPlaca(linha.item) || personalizada(linha.item)
  const minimo = quantidadeMinima(linha.item)
  const nome = linha.codigo ? `${linha.item.nome} ${linha.codigo}` : linha.item.nome
  const [ajustando, setAjustando] = useState(false)
  const [valor, setValor] = useState('')
  const centavos = valor.trim() ? paraCentavos(valor) : null
  const valorInvalido = valor.trim() !== '' && centavos === null
  const destino = linha.destinos
    ? linha.destinos.map((d, i) => `QR ${i + 1}: ${d.link ?? 'avaliação no Google'}`).join(' · ')
    : linha.lugar?.nome ?? linha.destino?.link ?? null

  return (
    <li className="grid gap-2 py-3 first:pt-0 last:pb-0">
      <div className="flex items-start gap-2">
        <div className="min-w-0 flex-1">
          <p className="font-medium">{linha.item.nome}{linha.codigo && <span className="ml-1 tabular-nums">{linha.codigo}</span>}</p>
          {destino && <p className="truncate text-xs text-muted-foreground">{destino}</p>}
          {!placa && minimo > 1 && <p className="text-xs text-muted-foreground">Mínimo {minimo}</p>}
        </div>
        <p className="text-right tabular-nums">
          <span className="block font-medium">{formatarCentavos(totalDaLinha(linha))}</span>
          {linha.valorCents !== null && <span className="block text-xs text-muted-foreground line-through">{formatarCentavos(linha.item.precoCents * linha.quantidade)}</span>}
        </p>
      </div>
      <div className="flex items-center gap-1">
        {!placa && <Quantidade nome={nome} valor={linha.quantidade} minimo={minimo} aoMudar={aoMudarQuantidade} />}
        <div className="ml-auto flex items-center">
          <Button variant="ghost" size="icon" aria-label={`Ajustar valor de ${nome}`} aria-pressed={ajustando} onClick={() => setAjustando((a) => !a)}>
            <Pencil aria-hidden />
          </Button>
          <Button variant="ghost" size="icon" aria-label={`Tirar ${nome}`} onClick={aoRemover}>
            <Trash2 aria-hidden />
          </Button>
        </div>
      </div>
      {ajustando && (
        <form
          className="flex gap-2"
          onSubmit={(e) => {
            e.preventDefault()
            if (valorInvalido) return
            aoMudarValor(centavos)
            setAjustando(false)
          }}
        >
          <Input
            aria-label={`Valor cobrado de ${nome}`}
            inputMode="decimal"
            placeholder={formatarCentavos(linha.item.precoCents * linha.quantidade)}
            aria-invalid={valorInvalido}
            value={valor}
            onChange={(e) => setValor(e.target.value)}
          />
          <Button type="submit" variant="outline" disabled={valorInvalido}>{valor.trim() ? 'Aplicar' : 'Tabela'}</Button>
        </form>
      )}
    </li>
  )
}

/**
 * Menos, o número e mais. O campo aceita digitar; ao sair, o valor abaixo do
 * mínimo volta para ele e o aviso aparece por alguns segundos.
 */
function Quantidade({ nome, valor, minimo, aoMudar }: { nome: string; valor: number; minimo: number; aoMudar: (q: number) => void }) {
  const [texto, setTexto] = useState(String(valor))
  const [aviso, setAviso] = useState(false)
  // O valor de fora mudou (mais, menos ou a correção do mínimo): o campo acompanha.
  const [valorDeAntes, setValorDeAntes] = useState(valor)
  if (valorDeAntes !== valor) {
    setValorDeAntes(valor)
    setTexto(String(valor))
  }
  useEffect(() => {
    if (!aviso) return
    const relogio = window.setTimeout(() => setAviso(false), 2_500)
    return () => window.clearTimeout(relogio)
  }, [aviso])

  function mudar(q: number) {
    if (q < minimo) setAviso(true)
    // O campo volta ao valor valendo; se o de fora mudar, a sincronização acima o alcança.
    setTexto(String(valor))
    aoMudar(q)
  }

  return (
    <div className="grid gap-1">
      <div className="flex items-center gap-1">
        <Button variant="outline" size="icon" aria-label={`Menos ${nome}`} onClick={() => mudar(valor - 1)}>
          <Minus aria-hidden />
        </Button>
        <Input
          aria-label={`Quantidade de ${nome}`}
          inputMode="numeric"
          className="w-14 text-center tabular-nums"
          value={texto}
          onChange={(e) => setTexto(e.target.value.replace(/\D/g, ''))}
          onBlur={() => mudar(Number(texto))}
          onKeyDown={(e) => {
            if (e.key === 'Enter') {
              e.preventDefault()
              mudar(Number(texto))
            }
          }}
        />
        <Button variant="outline" size="icon" aria-label={`Mais ${nome}`} onClick={() => mudar(valor + 1)}>
          <Plus aria-hidden />
        </Button>
      </div>
      <p role="status" className="min-h-4 text-xs text-destructive">{aviso ? `Mínimo ${minimo}` : ''}</p>
    </div>
  )
}
