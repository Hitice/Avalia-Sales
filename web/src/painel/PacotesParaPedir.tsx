import { useState } from 'react'
import { mensagemDoErro } from '@/api/cliente'
import { Erro, EstadoDaConsulta } from '@/componentes/Tela'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { formatarCentavos } from '@/dinheiro/dinheiro'
import { usePacotes, usePedir } from './consultas'

/** Os pacotes de placas à venda, com o preço por placa e o cupom. Pedir cria o pedido, que se paga em Pedidos. */
export function PacotesParaPedir({ aoPedir }: { aoPedir?: () => void }) {
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
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
        {pacotes.data?.map((p) => (
          <div key={p.id} className="grid gap-2 rounded-lg border p-3">
            <p className="text-lg font-semibold tabular-nums">{p.quantidade} placas</p>
            <p className="text-sm font-medium tabular-nums">{formatarCentavos(p.custoPorPlacaCents)} por placa</p>
            <p className="text-xs text-muted-foreground tabular-nums">{formatarCentavos(p.precoCents)} no total</p>
            <Button disabled={pedir.isPending} onClick={() => pedir.mutate({ pacoteId: p.id, cupom: cupom.trim() || null }, { onSuccess: () => { setCupom(''); aoPedir?.() } })}>Pedir</Button>
          </div>
        ))}
      </div>
    </div>
  )
}
