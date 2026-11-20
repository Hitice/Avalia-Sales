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
import { Textarea } from '@/components/ui/textarea'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { paraCentavos } from '@/dinheiro/dinheiro'
import { DialogoDaDevolucao } from './DialogoDaDevolucao'
import { useCancelarVendaDigital, useRevendasDaVendaDigital, useVendasDigitais, useVenderNoDigital, type VendaDigitalParcial } from './consultas'

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
  const cancelar = useCancelarVendaDigital()
  const vendasDigitais = useVendasDigitais(lote?.id ?? null)
  const [revendedorId, setRevendedorId] = useState<string | null>(null)
  const [valor, setValor] = useState('')
  const [canal, setCanal] = useState<CanalDigital>('MercadoLivre')
  const [pedido, setPedido] = useState('')
  const [codigos, setCodigos] = useState('')
  const [vendido, setVendido] = useState<{ codigoDeAtivacao: string; quantidade: number; lote: string } | null>(null)
  const [devolvendo, setDevolvendo] = useState<string | null>(null)
  const escolhida = revendedorId ?? revendas.data?.[0]?.id ?? null
  const centavos = paraCentavos(valor)
  const codigosDasPlacas = codigos.split(/[\s,;]+/).map((c) => c.trim().toUpperCase()).filter(Boolean)

  function fechar() {
    setVendido(null)
    setValor('')
    setPedido('')
    setCodigos('')
    vender.reset()
    aoFechar()
  }

  function lancar() {
    if (!lote || !escolhida || !centavos || codigosDasPlacas.length === 0) return
    vender.mutate({ id: lote.id, revendedorId: escolhida, valorCents: centavos, canal, codigosDasPlacas, pedidoNoCanal: pedido.trim() || null }, { onSuccess: (feito) => setVendido(feito as VendaDigitalParcial) })
  }

  const codigo = vendido?.codigoDeAtivacao
  return (
    <Dialog open={!!lote} onOpenChange={(aberto) => !aberto && fechar()}>
      <DialogContent>
        <DialogHeader><DialogTitle>{vendido ? `Pedido do ${vendido.lote} lançado` : `Venda digital do ${lote?.rotulo ?? ''}`}</DialogTitle></DialogHeader>
        {vendido && codigo ? (
          <div className="grid gap-4">
            <p className="text-sm">{vendido.quantidade} placas do {vendido.lote}. Mande o código junto com elas:</p>
            <p className="text-center font-mono text-3xl font-semibold tracking-widest">{codigo}</p>
            <CopiarLink rotulo="Endereço da ativação" endereco={enderecoDaAtivacao(codigo)} />
            <Button onClick={fechar}>Fechar</Button>
          </div>
        ) : (
          <form className="grid gap-4" onSubmit={(e) => { e.preventDefault(); lancar() }}>
            <Field>
              <FieldLabel htmlFor="codigos-digital">Códigos das placas</FieldLabel>
              <Textarea id="codigos-digital" value={codigos} onChange={(e) => setCodigos(e.target.value)} placeholder="WGQZ80, ABC123" aria-invalid={codigos !== '' && new Set(codigosDasPlacas).size !== codigosDasPlacas.length} />
            </Field>
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
              <FieldLabel htmlFor="valor-digital">Valor recebido pelo grupo</FieldLabel>
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
            <div className="grid gap-2 border-t pt-4">
              <p className="text-sm font-medium">Pedidos separados</p>
              {vendasDigitais.data?.length === 0 && <p className="text-sm text-muted-foreground">Nenhum pedido deste lote.</p>}
              {vendasDigitais.data?.map((grupo) => (
                <div key={grupo.grupoId} className="flex flex-wrap items-center justify-between gap-2">
                  <span className="font-mono text-sm">{grupo.codigoDeAtivacao} · {grupo.quantidade} placas</span>
                  <span className="flex items-center gap-2">
                    <CopiarLink rotulo="Copiar link" endereco={enderecoDaAtivacao(grupo.codigoDeAtivacao)} />
                    <Button type="button" variant="ghost" size="sm" className="text-destructive" onClick={() => setDevolvendo(grupo.grupoId)}>Devolução</Button>
                  </span>
                </div>
              ))}
            </div>
            <Erro erro={vender.error ? mensagemDoErro(vender.error) : null} />
            <Button type="submit" disabled={vender.isPending || !escolhida || !centavos || codigosDasPlacas.length === 0 || new Set(codigosDasPlacas).size !== codigosDasPlacas.length}>{vender.isPending ? 'Lançando...' : 'Lançar venda'}</Button>
          </form>
        )}
      </DialogContent>
      <DialogoDaDevolucao alvo={devolvendo} titulo="Devolução do pedido digital" devolver={cancelar} aoFechar={() => setDevolvendo(null)} />
    </Dialog>
  )
}
