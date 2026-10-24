import { useState } from 'react'
import { mensagemDoErro } from '@/api/cliente'
import { ROTULO_DO_CANCELAMENTO } from '@/api/rotulos'
import type { MotivoDaBaixa, MotivoDoCancelamento, PlacaDaVendaCancelada, VendaDaLista } from '@/api/tipos'
import { Erro } from '@/componentes/Tela'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle, DialogTrigger } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { formatarCentavos, paraCentavos } from '@/dinheiro/dinheiro'
import { useCancelarVenda, useCorrigirVenda, useCortesia, useDesfazerRecebimento, useEquipe, useReceberPorFora, useTrocarPlaca } from './consultas'

type Acao = 'Dinheiro' | 'OutroPix' | 'desfazer' | 'cortesia' | 'cancelar'

/** Por que a placa da venda sai: a antiga fica encerrada com o motivo. */
const DESTINO_DA_PLACA: Record<PlacaDaVendaCancelada, string> = {
  VoltaAoEstoque: 'Voltou: vai ao estoque',
  ComDefeito: 'Voltou com defeito',
  NaoVoltou: 'Não voltou',
}

const MOTIVO_DA_TROCA: Partial<Record<MotivoDaBaixa, string>> = {
  DefeitoDeImpressao: 'Defeito de impressão',
  Quebrada: 'Quebrada',
  Perdida: 'Perdida',
}

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
  const [motivo, setMotivo] = useState<MotivoDaBaixa>('DefeitoDeImpressao')
  const [porque, setPorque] = useState<MotivoDoCancelamento>('ErroDeCadastro')
  const [placaVolta, setPlacaVolta] = useState<PlacaDaVendaCancelada>('VoltaAoEstoque')
  const [observacao, setObservacao] = useState('')
  const receber = useReceberPorFora()
  const desfazer = useDesfazerRecebimento()
  const cancelar = useCancelarVenda()
  const cortesia = useCortesia()
  const trocar = useTrocarPlaca()
  const corrigir = useCorrigirVenda()
  const equipe = useEquipe()
  const [novoValor, setNovoValor] = useState('')
  const [novoVendedor, setNovoVendedor] = useState('')
  const mutacoes = [receber, desfazer, cancelar, cortesia, trocar, corrigir]
  const ocupado = mutacoes.some((m) => m.isPending)
  const erro = mutacoes.find((m) => m.error)?.error

  const viva = venda.pagamento === 'Pendente' || venda.pagamento === 'Paga'
  const ativa = venda.situacao === 'Registrada'
  const podeReceber = ativa && !venda.cortesia && !venda.doSistemaAnterior && venda.valorCents > 0 && !viva
  const recebidaPorFora = venda.pagamento === 'Paga' && (venda.pagoPor === 'Dinheiro' || venda.pagoPor === 'OutroPix')
  const podeDesfazerVenda = dono && ativa && !viva && !venda.doSistemaAnterior
  // Cancelar vale com cobrança viva: o Pix em aberto é cancelado e o pago volta ao cliente.
  const podeCancelar = dono && ativa && !venda.doSistemaAnterior
  // O vendedor troca na venda dele, com placa que está com ele; a API confere.
  const podeTrocar = ativa && !!venda.codigo
  if (!ativa || (!dono && !podeReceber && !podeTrocar && !podeCancelar)) return null

  const fechar = (aberto: boolean) => {
    setAberto(aberto)
    setConfirmando(null)
    setCodigo('')
    setMotivo('DefeitoDeImpressao')
    setPorque('ErroDeCadastro')
    setPlacaVolta('VoltaAoEstoque')
    setObservacao('')
    setNovoValor('')
    setNovoVendedor('')
    mutacoes.forEach((m) => m.reset())
  }
  const feito = { onSuccess: () => fechar(false) }
  const confirmar = (acao: Acao) => {
    if (acao === 'Dinheiro' || acao === 'OutroPix') receber.mutate({ vendaId: venda.id, meio: acao }, feito)
    else if (acao === 'desfazer') desfazer.mutate(venda.id, feito)
    else if (acao === 'cortesia') cortesia.mutate(venda.id, feito)
    else cancelar.mutate({ vendaId: venda.id, motivo: porque, placa: venda.codigo ? placaVolta : 'VoltaAoEstoque', observacao: observacao.trim() || null }, feito)
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

        {confirmando === 'cancelar' && (
          <div className="grid gap-3">
            <Select value={porque} onValueChange={(v) => setPorque(v as MotivoDoCancelamento)}>
              <SelectTrigger aria-label="Motivo do cancelamento" className="w-full"><SelectValue /></SelectTrigger>
              <SelectContent>
                {(Object.keys(ROTULO_DO_CANCELAMENTO) as MotivoDoCancelamento[]).map((m) => (
                  <SelectItem key={m} value={m}>{ROTULO_DO_CANCELAMENTO[m].charAt(0).toUpperCase() + ROTULO_DO_CANCELAMENTO[m].slice(1)}</SelectItem>
                ))}
              </SelectContent>
            </Select>
            {venda.codigo && (
              <Select value={placaVolta} onValueChange={(v) => setPlacaVolta(v as PlacaDaVendaCancelada)}>
                <SelectTrigger aria-label="A placa" className="w-full"><SelectValue /></SelectTrigger>
                <SelectContent>
                  {(Object.keys(DESTINO_DA_PLACA) as PlacaDaVendaCancelada[]).map((d) => <SelectItem key={d} value={d}>{DESTINO_DA_PLACA[d]}</SelectItem>)}
                </SelectContent>
              </Select>
            )}
            <Input aria-label="Observação" maxLength={300} placeholder="Observação (opcional)" value={observacao} onChange={(e) => setObservacao(e.target.value)} />
            {venda.pagamento === 'Paga' && <p className="text-sm text-muted-foreground">O pagamento volta ao cliente{recebidaPorFora ? '; devolva por fora' : ' pelo Pagar.me'}.</p>}
          </div>
        )}
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
            {podeTrocar && (
              <form
                className="grid gap-2"
                onSubmit={(e) => {
                  e.preventDefault()
                  trocar.mutate({ vendaId: venda.id, codigo, motivo }, feito)
                }}
              >
                <Label htmlFor={`troca-${venda.id}`}>Trocar a placa</Label>
                <Select value={motivo} onValueChange={(v) => setMotivo(v as MotivoDaBaixa)}>
                  <SelectTrigger aria-label="Motivo da troca" className="w-full"><SelectValue /></SelectTrigger>
                  <SelectContent>
                    {(Object.keys(MOTIVO_DA_TROCA) as MotivoDaBaixa[]).map((m) => <SelectItem key={m} value={m}>{MOTIVO_DA_TROCA[m]}</SelectItem>)}
                  </SelectContent>
                </Select>
                <div className="flex gap-2">
                  <Input id={`troca-${venda.id}`} value={codigo} onChange={(e) => setCodigo(e.target.value)} placeholder="Código da placa nova" autoComplete="off" className="uppercase placeholder:normal-case" />
                  <Button type="submit" variant="outline" disabled={ocupado || codigo.trim().length < 6}>Trocar</Button>
                </div>
                <p className="text-xs text-muted-foreground">A placa {venda.codigo} será encerrada.</p>
              </form>
            )}
            {podeCancelar && (
              <form
                className="grid gap-2"
                onSubmit={(e) => {
                  e.preventDefault()
                  corrigir.mutate({ vendaId: venda.id, valorCents: novoValor.trim() ? paraCentavos(novoValor) : null, vendedorId: novoVendedor || null }, feito)
                }}
              >
                <Label htmlFor={`valor-${venda.id}`}>Corrigir a venda</Label>
                <div className="flex gap-2">
                  <Input id={`valor-${venda.id}`} inputMode="decimal" placeholder={formatarCentavos(venda.valorCents)} value={novoValor} onChange={(e) => setNovoValor(e.target.value)} disabled={viva} />
                  <Select value={novoVendedor} onValueChange={setNovoVendedor}>
                    <SelectTrigger aria-label="Vendedor" className="w-full"><SelectValue placeholder={venda.vendedor} /></SelectTrigger>
                    <SelectContent>{(equipe.data ?? []).filter((v) => v.ativo && v.id !== venda.vendedorId).map((v) => <SelectItem key={v.id} value={v.id}>{v.nome}</SelectItem>)}</SelectContent>
                  </Select>
                </div>
                <Button type="submit" variant="outline" className="justify-self-start" disabled={ocupado || (!novoValor.trim() && !novoVendedor) || (!!novoValor.trim() && paraCentavos(novoValor) === null)}>Salvar a correção</Button>
              </form>
            )}
            {(podeDesfazerVenda || podeCancelar) && (
              <div className="flex flex-wrap gap-2">
                {podeDesfazerVenda && !venda.cortesia && <Button variant="outline" onClick={() => setConfirmando('cortesia')}>Dar de cortesia</Button>}
                {podeCancelar && <Button variant="outline" onClick={() => setConfirmando('cancelar')}>Cancelar a venda</Button>}
              </div>
            )}
          </div>
        )}
      </DialogContent>
    </Dialog>
  )
}
