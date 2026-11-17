import { useState } from 'react'
import { mensagemDoErro } from '@/api/cliente'
import { ROTULO_DO_CANAL } from '@/api/rotulos'
import type { CanalDigital, Lote } from '@/api/tipos'
import { CopiarLink } from '@/balcao/BotoesDeNfc'
import { Erro } from '@/componentes/Tela'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Field, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { paraCentavos } from '@/dinheiro/dinheiro'
import { useRevendasDaVendaDigital, useVenderNoDigital } from './consultas'

const CANAIS = Object.keys(ROTULO_DO_CANAL) as CanalDigital[]

/** O endereço que vai com o lote para o comprador: a página de ativação já com o código. */
const enderecoDaAtivacao = (codigo: string) => `${window.location.origin}/ativar?codigo=${codigo}`

/**
 * A baixa manual do lote vendido no Mercado Livre, TikTok Shop ou Shopee: a revenda (a do grupo da operadora vem
 * escolhida), o valor recebido pelo lote inteiro e o canal. Depois mostra o código e o endereço para mandar ao comprador.
 */
export function ModalDaVendaDigital({ lote, aoFechar }: { lote: Lote | null; aoFechar: () => void }) {
  const revendas = useRevendasDaVendaDigital()
  const vender = useVenderNoDigital()
  const [revendedorId, setRevendedorId] = useState<string | null>(null)
  const [valor, setValor] = useState('')
  const [canal, setCanal] = useState<CanalDigital>('MercadoLivre')
  const [pedido, setPedido] = useState('')
  const [vendido, setVendido] = useState<Lote | null>(null)
  const escolhida = revendedorId ?? revendas.data?.[0]?.id ?? null
  const centavos = paraCentavos(valor)

  function fechar() {
    setVendido(null)
    setValor('')
    setPedido('')
    vender.reset()
    aoFechar()
  }

  function lancar() {
    if (!lote || !escolhida || !centavos) return
    vender.mutate({ id: lote.id, revendedorId: escolhida, valorCents: centavos, canal, pedidoNoCanal: pedido.trim() || null }, { onSuccess: (feito) => setVendido(feito as Lote) })
  }

  const codigo = vendido?.codigoDeAtivacao ?? lote?.codigoDeAtivacao
  return (
    <Dialog open={!!lote} onOpenChange={(aberto) => !aberto && fechar()}>
      <DialogContent>
        <DialogHeader><DialogTitle>{vendido ? `Lote ${vendido.rotulo} lançado` : `Venda digital do ${lote?.rotulo ?? ''}`}</DialogTitle></DialogHeader>
        {vendido && codigo ? (
          <div className="grid gap-4">
            <p className="text-sm">Mande ao comprador junto com as placas:</p>
            <p className="text-center font-mono text-3xl font-semibold tracking-widest">{codigo}</p>
            <CopiarLink rotulo="Endereço da ativação" endereco={enderecoDaAtivacao(codigo)} />
            <Button onClick={fechar}>Fechar</Button>
          </div>
        ) : (
          <form className="grid gap-4" onSubmit={(e) => { e.preventDefault(); lancar() }}>
            <Field>
              <FieldLabel htmlFor="revenda-digital">Revenda</FieldLabel>
              <Select value={escolhida ?? undefined} onValueChange={setRevendedorId}>
                <SelectTrigger id="revenda-digital" className="w-full"><SelectValue placeholder="Escolha a revenda" /></SelectTrigger>
                <SelectContent>
                  {revendas.data?.map((r) => <SelectItem key={r.id} value={r.id}>{r.nome}</SelectItem>)}
                </SelectContent>
              </Select>
            </Field>
            <Field>
              <FieldLabel htmlFor="valor-digital">Valor recebido</FieldLabel>
              <Input id="valor-digital" inputMode="decimal" placeholder="R$ 0,00" value={valor} onChange={(e) => setValor(e.target.value)} aria-invalid={!!valor && !centavos} />
            </Field>
            <Field>
              <FieldLabel htmlFor="canal-digital">Canal</FieldLabel>
              <Select value={canal} onValueChange={(v) => setCanal(v as CanalDigital)}>
                <SelectTrigger id="canal-digital" className="w-full"><SelectValue /></SelectTrigger>
                <SelectContent>
                  {CANAIS.map((c) => <SelectItem key={c} value={c}>{ROTULO_DO_CANAL[c]}</SelectItem>)}
                </SelectContent>
              </Select>
            </Field>
            <Field>
              <FieldLabel htmlFor="pedido-digital">Pedido no canal</FieldLabel>
              <Input id="pedido-digital" maxLength={60} value={pedido} onChange={(e) => setPedido(e.target.value)} />
            </Field>
            <Erro erro={vender.error ? mensagemDoErro(vender.error) : null} />
            <Button type="submit" disabled={vender.isPending || !escolhida || !centavos}>{vender.isPending ? 'Lançando...' : 'Lançar venda'}</Button>
          </form>
        )}
      </DialogContent>
    </Dialog>
  )
}
