import { useState } from 'react'
import { mensagemDoErro } from '@/api/cliente'
import { formatarDataHora, rotuloDoPedido } from '@/api/rotulos'
import { Cabecalho, Erro, EstadoDaConsulta, Rolagem } from '@/componentes/Tela'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatarCentavos } from '@/dinheiro/dinheiro'
import { useAtivarLote, useCancelarPedido, usePagarPedido, usePedidos } from './consultas'
import { PacotesParaPedir } from './PacotesParaPedir'

/** Pedir placas e ativar o lote que chegou. */
export function TelaDePedidosDePlacas() {
  const pedidos = usePedidos()
  const cancelar = useCancelarPedido()
  const ativar = useAtivarLote()
  const pagar = usePagarPedido()
  const [numero, setNumero] = useState('')

  return (
    <div className="grid grid-cols-1 gap-4 xl:grid-cols-[minmax(0,1fr)_20rem]">
      <div className="grid gap-4">
        <Cabecalho titulo="Pedidos" />
        <Card>
          <CardHeader><CardTitle>Pacotes</CardTitle></CardHeader>
          <CardContent>
            <PacotesParaPedir />
          </CardContent>
        </Card>
        <Card>
          <CardHeader><CardTitle>Meus pedidos</CardTitle></CardHeader>
          <CardContent>
            <Erro erro={cancelar.error || pagar.error ? mensagemDoErro(cancelar.error ?? pagar.error) : null} />
            <EstadoDaConsulta carregando={pedidos.isPending} erro={pedidos.error} vazio={pedidos.data?.length === 0} textoVazio="Nenhum pedido ainda." />
            {pedidos.data && pedidos.data.length > 0 && (
              <Rolagem>
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Pedido em</TableHead>
                      <TableHead className="text-right">Placas</TableHead>
                      <TableHead className="text-right">Valor</TableHead>
                      <TableHead>Situação</TableHead>
                      <TableHead>Lote</TableHead>
                      <TableHead><span className="sr-only">Ação</span></TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {pedidos.data.map((p) => (
                      <TableRow key={p.id}>
                        <TableCell>{formatarDataHora(p.criadoEm)}</TableCell>
                        <TableCell className="text-right tabular-nums">{p.quantidade}</TableCell>
                        <TableCell className="text-right tabular-nums">
                          {formatarCentavos(p.valorCents)}
                          {p.cupom && <span className="block text-xs text-muted-foreground">cupom {p.cupom}, menos {formatarCentavos(p.descontoCents)}</span>}
                        </TableCell>
                        <TableCell><Badge variant={p.situacao === 'Ativado' ? 'default' : p.situacao === 'Cancelado' ? 'outline' : 'secondary'}>{rotuloDoPedido(p)}</Badge></TableCell>
                        <TableCell>{p.lote ?? '-'}</TableCell>
                        <TableCell className="text-right">
                          {p.situacao === 'AguardandoPagamento' && (
                            <div className="flex justify-end gap-2">
                              <Button
                                disabled={pagar.isPending}
                                onClick={async () => {
                                  // A página do Pagar.me abre em outra aba; o pedido fica pago aqui quando o pagamento cai.
                                  const aba = window.open('', '_blank')
                                  const url = p.urlDePagamento ?? (await pagar.mutateAsync(p.id).catch(() => null))?.url
                                  if (url && aba) aba.location.href = url
                                  else aba?.close()
                                }}
                              >
                                Pagar
                              </Button>
                              {!p.urlDePagamento && <Button variant="ghost" onClick={() => cancelar.mutate(p.id)}>Cancelar</Button>}
                            </div>
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
      </div>
      <Card className="self-start">
        <CardHeader><CardTitle>Ativar lote</CardTitle></CardHeader>
        <CardContent>
          <form
            className="grid gap-4"
            onSubmit={(e) => {
              e.preventDefault()
              ativar.mutate(numero, { onSuccess: () => setNumero('') })
            }}
          >
            <Erro erro={ativar.error ? mensagemDoErro(ativar.error) : null} />
            {ativar.data && (
              <Alert><AlertDescription>Lote {ativar.data.lote} ativado: {ativar.data.placas} placas no estoque, de {ativar.data.primeiroCodigo} a {ativar.data.ultimoCodigo}.</AlertDescription></Alert>
            )}
            <Field>
              <FieldLabel htmlFor="lote">Lote</FieldLabel>
              <Input id="lote" placeholder="LT-0001" value={numero} onChange={(e) => setNumero(e.target.value)} />
            </Field>
            <Button type="submit" disabled={!numero.trim() || ativar.isPending}>{ativar.isPending ? 'Ativando...' : 'Ativar lote'}</Button>
          </form>
        </CardContent>
      </Card>
    </div>
  )
}
