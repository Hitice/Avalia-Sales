import { useState } from 'react'
import { formatarDataHora, rotuloDoPedido, TOM_DO_PEDIDO } from '@/api/rotulos'
import type { Pedido } from '@/api/tipos'
import { AcoesDaLinha } from '@/componentes/AcoesDaLinha'
import { ListaPaginada } from '@/componentes/ListaPaginada'
import { Cabecalho, Erro, Rolagem } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Field, FieldLabel } from '@/components/ui/field'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatarCentavos } from '@/dinheiro/dinheiro'
import { useDespachar, useIniciarProducao, useLotesLivres, usePedidosDaOperadora, useRegistrarPagamento } from './consultas'

/** A fila da operadora: pedido pago ganha um lote livre da mesma quantidade. */
export function TelaDePedidos() {
  const pedidos = usePedidosDaOperadora()
  const pagar = useRegistrarPagamento()
  const despachar = useDespachar()
  const producao = useIniciarProducao()
  const [despachando, setDespachando] = useState<Pedido | null>(null)

  return (
    <>
      <Cabecalho titulo="Pedidos" />
      <Erro erro={pagar.error ?? producao.error} />
      <Card className="mt-3">
        <CardContent>
          <ListaPaginada lista={pedidos} textoVazio="Nenhum pedido ainda." nomeDaBusca="Buscar nos pedidos" placeholder="Revendedor, cupom ou lote">
            {(itens) => (
              <Rolagem>
                <Table semBusca>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Revendedor</TableHead>
                      <TableHead tipo="numero">Placas</TableHead>
                      <TableHead tipo="numero">Valor</TableHead>
                      <TableHead tipo="data">Pedido em</TableHead>
                      <TableHead tipo="situacao">Situação</TableHead>
                      <TableHead>Lote</TableHead>
                      <TableHead tipo="acoes" />
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {itens.map((pedido) => {
                      const aDespachar = pedido.situacao === 'Pago' || pedido.situacao === 'EmProducao'
                      const semLote = aDespachar && !pedido.haLoteLivre
                      return (
                        <TableRow key={pedido.id}>
                          <TableCell className="min-w-32 font-medium whitespace-normal">{pedido.revendedor}</TableCell>
                          <TableCell>{pedido.quantidade}</TableCell>
                          <TableCell>{formatarCentavos(pedido.valorCents)}</TableCell>
                          <TableCell>{formatarDataHora(pedido.criadoEm)}</TableCell>
                          <TableCell><Badge variant={TOM_DO_PEDIDO[pedido.situacao]}>{rotuloDoPedido(pedido)}</Badge></TableCell>
                          <TableCell>{pedido.lote ?? (semLote ? <span className="text-muted-foreground">Gere um lote de {pedido.quantidade}</span> : '-')}</TableCell>
                          <TableCell>
                            <AcoesDaLinha
                              nome={`pedido de ${pedido.revendedor}`}
                              principal={
                                pedido.situacao === 'AguardandoPagamento'
                                  ? { rotulo: 'Registrar pagamento', desabilitada: pagar.isPending, aoClicar: () => pagar.mutate(pedido.id) }
                                  : aDespachar && !semLote && { rotulo: 'Despachar', aoClicar: () => { despachar.reset(); setDespachando(pedido) } }
                              }
                              outras={[
                                pedido.situacao === 'Pago' && { rotulo: 'Iniciar produção', desabilitada: producao.isPending, aoClicar: () => producao.mutate(pedido.id) },
                              ]}
                            />
                          </TableCell>
                        </TableRow>
                      )
                    })}
                  </TableBody>
                </Table>
              </Rolagem>
            )}
          </ListaPaginada>
        </CardContent>
      </Card>
      {despachando && (
        <Despacho pedido={despachando} ocupado={despachar.isPending} erro={despachar.error}
          aoFechar={() => setDespachando(null)}
          aoDespachar={(lote, retirada) => despachar.mutate({ id: despachando.id, lote, retirada }, { onSuccess: () => setDespachando(null) })} />
      )}
    </>
  )
}

/** O lote livre da mesma quantidade vai para a revenda: enviado pelo correio ou pronto para ela retirar. */
function Despacho({ pedido, ocupado, erro, aoFechar, aoDespachar }: {
  pedido: Pedido
  ocupado: boolean
  erro: unknown
  aoFechar: () => void
  aoDespachar: (lote: number, retirada: boolean) => void
}) {
  const livres = useLotesLivres(pedido.quantidade)
  const lotes = livres.data?.itens ?? []
  const [escolhido, setLote] = useState('')
  // Com um lote só, ele já vem escolhido.
  const lote = escolhido || (lotes.length === 1 ? String(lotes[0].numero) : '')
  return (
    <Dialog open onOpenChange={(aberto) => !aberto && aoFechar()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Despachar pedido</DialogTitle>
          <DialogDescription>{pedido.revendedor}, {pedido.quantidade} placas.</DialogDescription>
        </DialogHeader>
        <Erro erro={erro} />
        <Field>
          <FieldLabel htmlFor="lote-do-despacho">Lote</FieldLabel>
          <Select value={lote} onValueChange={setLote}>
            <SelectTrigger id="lote-do-despacho" className="w-full"><SelectValue placeholder="Escolha" /></SelectTrigger>
            <SelectContent>
              {lotes.map((l) => <SelectItem key={l.id} value={String(l.numero)}>{l.rotulo}</SelectItem>)}
            </SelectContent>
          </Select>
        </Field>
        <DialogFooter>
          <Button variant="outline" disabled={!lote || ocupado} onClick={() => aoDespachar(Number(lote), true)}>Pronto para retirada</Button>
          <Button disabled={!lote || ocupado} onClick={() => aoDespachar(Number(lote), false)}>Despachar</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
