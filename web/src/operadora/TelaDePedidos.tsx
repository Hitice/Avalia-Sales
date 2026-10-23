import { useState } from 'react'
import { formatarDataHora, ROTULO_DO_PEDIDO } from '@/api/rotulos'
import type { Pedido } from '@/api/tipos'
import { Cabecalho, Erro, EstadoDaConsulta, Rolagem } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatarCentavos } from '@/dinheiro/dinheiro'
import { useDespachar, useIniciarProducao, useLotes, usePedidosDaOperadora, useRegistrarPagamento } from './consultas'

/** A fila da operadora: pedido pago ganha um lote livre da mesma quantidade. */
export function TelaDePedidos() {
  const pedidos = usePedidosDaOperadora()
  const lotes = useLotes()
  const pagar = useRegistrarPagamento()
  const despachar = useDespachar()
  const producao = useIniciarProducao()
  const [loteEscolhido, setLoteEscolhido] = useState<Record<string, string>>({})
  const livres = (pedido: Pedido) => (lotes.data ?? []).filter((l) => !l.despachadoEm && l.quantidade === pedido.quantidade)

  return (
    <>
      <Cabecalho titulo="Pedidos" />
      <Erro erro={pagar.error ?? despachar.error ?? producao.error} />
      <Card className="mt-3">
        <CardContent>
          <EstadoDaConsulta carregando={pedidos.isPending} erro={pedidos.error} vazio={pedidos.data?.length === 0} textoVazio="Nenhum pedido ainda." />
          {pedidos.data && pedidos.data.length > 0 && (
            <Rolagem>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Revendedor</TableHead>
                    <TableHead className="text-right">Placas</TableHead>
                    <TableHead className="text-right">Valor</TableHead>
                    <TableHead>Pedido em</TableHead>
                    <TableHead>Situação</TableHead>
                    <TableHead>Lote</TableHead>
                    <TableHead><span className="sr-only">Ação</span></TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {pedidos.data.map((pedido) => (
                    <TableRow key={pedido.id}>
                      <TableCell className="min-w-32 font-medium whitespace-normal">{pedido.revendedor}</TableCell>
                      <TableCell className="text-right tabular-nums">{pedido.quantidade}</TableCell>
                      <TableCell className="text-right tabular-nums">{formatarCentavos(pedido.valorCents)}</TableCell>
                      <TableCell>{formatarDataHora(pedido.criadoEm)}</TableCell>
                      <TableCell><Badge variant={pedido.situacao === 'Ativado' ? 'default' : 'secondary'}>{ROTULO_DO_PEDIDO[pedido.situacao]}</Badge></TableCell>
                      <TableCell>{pedido.lote ?? '-'}</TableCell>
                      <TableCell className="text-right">
                        {pedido.situacao === 'AguardandoPagamento' && (
                          <Button variant="outline" disabled={pagar.isPending} onClick={() => pagar.mutate(pedido.id)}>Registrar pagamento</Button>
                        )}
                        {(pedido.situacao === 'Pago' || pedido.situacao === 'EmProducao') && (
                          livres(pedido).length === 0
                            ? (
                                <div className="flex items-center justify-end gap-2">
                                  {pedido.situacao === 'Pago' && <Button variant="outline" disabled={producao.isPending} onClick={() => producao.mutate(pedido.id)}>Iniciar produção</Button>}
                                  <span className="text-sm text-muted-foreground">Gere um lote de {pedido.quantidade}</span>
                                </div>
                              )
                            : (
                                <div className="flex justify-end gap-2">
                                  {pedido.situacao === 'Pago' && <Button variant="outline" disabled={producao.isPending} onClick={() => producao.mutate(pedido.id)}>Iniciar produção</Button>}
                                  <Select value={loteEscolhido[pedido.id] ?? ''} onValueChange={(v) => setLoteEscolhido({ ...loteEscolhido, [pedido.id]: v })}>
                                    <SelectTrigger aria-label="Lote" className="w-32"><SelectValue placeholder="Lote" /></SelectTrigger>
                                    <SelectContent>
                                      {livres(pedido).map((l) => <SelectItem key={l.id} value={String(l.numero)}>{l.rotulo}</SelectItem>)}
                                    </SelectContent>
                                  </Select>
                                  <Button
                                    disabled={!loteEscolhido[pedido.id] || despachar.isPending}
                                    onClick={() => despachar.mutate({ id: pedido.id, lote: Number(loteEscolhido[pedido.id]) })}
                                  >
                                    Despachar
                                  </Button>
                                </div>
                              )
                        )}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </Rolagem>
          )}
        </CardContent>
      </Card>
    </>
  )
}
