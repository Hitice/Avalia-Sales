import { useState } from 'react'
import { mensagemDoErro } from '@/api/cliente'
import { formatarDataHora, ROTULO_DO_PEDIDO } from '@/api/rotulos'
import { Cabecalho, Erro, EstadoDaConsulta, Rolagem } from '@/componentes/Tela'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatarCentavos } from '@/dinheiro/dinheiro'
import { useAtivarLote, useCancelarPedido, usePacotes, usePedidos, usePedir } from './consultas'

/** Pedir placas e ativar o lote que chegou. */
export function TelaDePedidosDePlacas() {
  const pacotes = usePacotes()
  const pedidos = usePedidos()
  const pedir = usePedir()
  const cancelar = useCancelarPedido()
  const ativar = useAtivarLote()
  const [numero, setNumero] = useState('')

  return (
    <div className="grid gap-4 lg:grid-cols-[1fr_20rem]">
      <div className="grid gap-4">
        <Cabecalho titulo="Pedidos de placas" />
        <Card>
          <CardHeader><CardTitle>Pacotes</CardTitle></CardHeader>
          <CardContent className="grid gap-3">
            <Erro erro={pedir.error ? mensagemDoErro(pedir.error) : null} />
            <EstadoDaConsulta carregando={pacotes.isPending} erro={pacotes.error} vazio={pacotes.data?.length === 0} textoVazio="Nenhum pacote à venda agora." />
            <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
              {pacotes.data?.map((p) => (
                <div key={p.id} className="grid gap-2 rounded-lg border p-3">
                  <p className="text-lg font-semibold tabular-nums">{p.quantidade} placas</p>
                  <p className="text-sm tabular-nums">{formatarCentavos(p.precoCents)}</p>
                  <p className="text-xs text-muted-foreground tabular-nums">{formatarCentavos(p.custoPorPlacaCents)} por placa</p>
                  <Button disabled={pedir.isPending} onClick={() => pedir.mutate(p.id)}>Pedir</Button>
                </div>
              ))}
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardHeader><CardTitle>Meus pedidos</CardTitle></CardHeader>
          <CardContent>
            <Erro erro={cancelar.error ? mensagemDoErro(cancelar.error) : null} />
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
                        <TableCell className="text-right tabular-nums">{formatarCentavos(p.valorCents)}</TableCell>
                        <TableCell><Badge variant={p.situacao === 'Ativado' ? 'default' : 'secondary'}>{ROTULO_DO_PEDIDO[p.situacao]}</Badge></TableCell>
                        <TableCell>{p.lote ?? '-'}</TableCell>
                        <TableCell className="text-right">
                          {p.situacao === 'AguardandoPagamento' && <Button variant="ghost" onClick={() => cancelar.mutate(p.id)}>Cancelar</Button>}
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
              <FieldLabel htmlFor="lote">Número na caixa</FieldLabel>
              <Input id="lote" placeholder="LT-0001" value={numero} onChange={(e) => setNumero(e.target.value)} />
            </Field>
            <Button type="submit" disabled={!numero.trim() || ativar.isPending}>{ativar.isPending ? 'Ativando...' : 'Ativar lote'}</Button>
          </form>
        </CardContent>
      </Card>
    </div>
  )
}
