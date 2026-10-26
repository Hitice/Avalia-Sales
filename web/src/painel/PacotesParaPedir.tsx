import { useState } from 'react'
import { mensagemDoErro } from '@/api/cliente'
import { Erro, EstadoDaConsulta } from '@/componentes/Tela'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { formatarCentavos } from '@/dinheiro/dinheiro'
import { usePacotes, usePedir } from './consultas'

/** Os pacotes de placas à venda, com o preço por placa e o cupom. Pedir cria o pedido, que se paga em Pedidos. */
export function PacotesParaPedir() {
  const pacotes = usePacotes()
  const pedir = usePedir()
  const [cupom, setCupom] = useState('')

  return (
    <div className="grid gap-3">
      <Erro erro={pedir.error ? mensagemDoErro(pedir.error) : null} />
      <div className="flex max-w-xs items-end gap-2">
        <Input aria-label="Cupom de desconto" placeholder="Cupom" className="uppercase" value={cupom} onChange={(e) => setCupom(e.target.value)} />
      </div>
      <EstadoDaConsulta carregando={pacotes.isPending} erro={pacotes.error} vazio={pacotes.data?.length === 0} textoVazio="Nenhum pacote à venda agora." />
      {/* Uma linha por pacote, como numa tabela de preço: quantidade, preço por placa, total e o botão. */}
      <ul aria-label="Pacotes de placas" className="divide-y border-y">
        {pacotes.data?.map((p) => (
          <li key={p.id} className="flex items-center gap-3 py-3">
            <span className="grid min-w-0 flex-1 gap-0.5 sm:grid-cols-3 sm:items-center sm:gap-4">
              <span className="font-medium tabular-nums">{p.quantidade} placas</span>
              <span className="tabular-nums">{formatarCentavos(p.custoPorPlacaCents)} por placa</span>
              <span className="text-xs text-muted-foreground tabular-nums sm:text-sm">{formatarCentavos(p.precoCents)} no total</span>
            </span>
            <Button variant="outline" disabled={pedir.isPending} onClick={() => pedir.mutate({ pacoteId: p.id, cupom: cupom.trim() || null }, { onSuccess: () => setCupom('') })}>Pedir</Button>
          </li>
        ))}
      </ul>
    </div>
  )
}
