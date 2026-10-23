import { useState } from 'react'
import { mensagemDoErro } from '@/api/cliente'
import type { VendaDaLista } from '@/api/tipos'
import { Erro } from '@/componentes/Tela'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle, DialogTrigger } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { formatarCentavos } from '@/dinheiro/dinheiro'
import { useCancelarVenda, useCortesia, useDesfazerRecebimento, useReceberPorFora, useTrocarPlaca } from './consultas'

type Acao = 'Dinheiro' | 'OutroPix' | 'desfazer' | 'cortesia' | 'cancelar'

const CONFIRMACAO: Record<Acao, string> = {
  Dinheiro: 'Confirmar: recebido em dinheiro',
  OutroPix: 'Confirmar: recebido por outro Pix',
  desfazer: 'Confirmar: desfazer o recebimento',
  cortesia: 'Confirmar: dar de cortesia',
  cancelar: 'Confirmar: cancelar a venda',
}

/**
 * O que se faz com a venda depois do balcão. A API recusa o que não cabe
 * (Pix em aberto, comissão já repassada); a tela só esconde o que nunca cabe.
 */
export function AcoesDaVenda({ venda, dono }: { venda: VendaDaLista; dono: boolean }) {
  const [aberto, setAberto] = useState(false)
  const [confirmando, setConfirmando] = useState<Acao | null>(null)
  const [codigo, setCodigo] = useState('')
  const receber = useReceberPorFora()
  const desfazer = useDesfazerRecebimento()
  const cancelar = useCancelarVenda()
  const cortesia = useCortesia()
  const trocar = useTrocarPlaca()
  const mutacoes = [receber, desfazer, cancelar, cortesia, trocar]
  const ocupado = mutacoes.some((m) => m.isPending)
  const erro = mutacoes.find((m) => m.error)?.error

  const viva = venda.pagamento === 'Pendente' || venda.pagamento === 'Paga'
  const ativa = venda.situacao === 'Registrada'
  const podeReceber = ativa && !venda.cortesia && !venda.doSistemaAnterior && venda.valorCents > 0 && !viva
  const recebidaPorFora = venda.pagamento === 'Paga' && (venda.pagoPor === 'Dinheiro' || venda.pagoPor === 'OutroPix')
  const podeDesfazerVenda = dono && ativa && !viva && !venda.doSistemaAnterior
  if (!ativa || (!dono && !podeReceber)) return null

  const fechar = (aberto: boolean) => {
    setAberto(aberto)
    setConfirmando(null)
    setCodigo('')
    mutacoes.forEach((m) => m.reset())
  }
  const feito = { onSuccess: () => fechar(false) }
  const confirmar = (acao: Acao) => {
    if (acao === 'Dinheiro' || acao === 'OutroPix') receber.mutate({ vendaId: venda.id, meio: acao }, feito)
    else if (acao === 'desfazer') desfazer.mutate(venda.id, feito)
    else if (acao === 'cortesia') cortesia.mutate(venda.id, feito)
    else cancelar.mutate(venda.id, feito)
  }

  return (
    <Dialog open={aberto} onOpenChange={fechar}>
      <DialogTrigger asChild>
        <Button variant="ghost" aria-label={`Mais ações da placa ${venda.codigo}`}>Mais</Button>
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Placa {venda.codigo}</DialogTitle>
          <DialogDescription>{venda.cliente}, {formatarCentavos(venda.valorCents)}{venda.cortesia ? ', cortesia' : ''}</DialogDescription>
        </DialogHeader>
        <Erro erro={erro ? mensagemDoErro(erro) : null} />
        {venda.pagamento === 'Pendente' && <p className="text-muted-foreground">Pix em aberto.</p>}

        {confirmando ? (
          <DialogFooter>
            <Button variant="ghost" onClick={() => setConfirmando(null)}>Voltar</Button>
            <Button variant={confirmando === 'Dinheiro' || confirmando === 'OutroPix' ? 'default' : 'destructive'} disabled={ocupado} onClick={() => confirmar(confirmando)}>
              {CONFIRMACAO[confirmando]}
            </Button>
          </DialogFooter>
        ) : (
          <div className="grid gap-4">
            {podeReceber && (
              <div className="grid gap-2">
                <p className="font-medium">Recebido no balcão</p>
                <div className="flex flex-wrap gap-2">
                  <Button variant="outline" onClick={() => setConfirmando('Dinheiro')}>Em dinheiro</Button>
                  <Button variant="outline" onClick={() => setConfirmando('OutroPix')}>Por outro Pix</Button>
                </div>
              </div>
            )}
            {dono && recebidaPorFora && (
              <Button variant="outline" onClick={() => setConfirmando('desfazer')}>Desfazer o recebimento</Button>
            )}
            {dono && (
              <form
                className="grid gap-2"
                onSubmit={(e) => {
                  e.preventDefault()
                  trocar.mutate({ vendaId: venda.id, codigo }, feito)
                }}
              >
                <Label htmlFor={`troca-${venda.id}`}>Trocar a placa</Label>
                <div className="flex gap-2">
                  <Input id={`troca-${venda.id}`} value={codigo} onChange={(e) => setCodigo(e.target.value)} placeholder="Código da placa nova" autoComplete="off" className="uppercase placeholder:normal-case" />
                  <Button type="submit" variant="outline" disabled={ocupado || codigo.trim().length < 6}>Trocar</Button>
                </div>
                <p className="text-xs text-muted-foreground">A placa {venda.codigo} será encerrada.</p>
              </form>
            )}
            {podeDesfazerVenda && (
              <div className="flex flex-wrap gap-2">
                {!venda.cortesia && <Button variant="outline" onClick={() => setConfirmando('cortesia')}>Dar de cortesia</Button>}
                <Button variant="outline" onClick={() => setConfirmando('cancelar')}>Cancelar a venda</Button>
              </div>
            )}
          </div>
        )}
      </DialogContent>
    </Dialog>
  )
}
