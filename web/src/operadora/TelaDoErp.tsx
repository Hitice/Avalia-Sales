import { mensagemDoErro } from '@/api/cliente'
import { formatarDataHora } from '@/api/rotulos'
import { Cabecalho, Erro, EstadoDaConsulta, Rolagem } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatarCentavos } from '@/dinheiro/dinheiro'
import { useFilaDoErp, useReenviarAoErp } from './consultas'

const ROTULO = { Pendente: 'Na fila', Enviado: 'No ERP', Parado: 'Parado' } as const

/** Os pedidos de placas pagos a caminho do Protheus, como pedido de venda. */
export function TelaDoErp() {
  const fila = useFilaDoErp()
  const reenviar = useReenviarAoErp()

  return (
    <>
      <Cabecalho titulo="ERP" />
      <Erro erro={reenviar.error ? mensagemDoErro(reenviar.error) : null} />
      <Card className="mt-3">
        <CardContent>
          <EstadoDaConsulta carregando={fila.isPending} erro={fila.error} vazio={fila.data?.length === 0} textoVazio="Nenhum pedido pago ainda." />
          {fila.data && fila.data.length > 0 && (
            <Rolagem>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Pago em</TableHead>
                    <TableHead>Revendedor</TableHead>
                    <TableHead className="text-right">Placas</TableHead>
                    <TableHead className="text-right">Valor</TableHead>
                    <TableHead>Situação</TableHead>
                    <TableHead>Pedido de venda</TableHead>
                    <TableHead><span className="sr-only">Ação</span></TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {fila.data.map((e) => (
                    <TableRow key={e.pedidoId}>
                      <TableCell>{formatarDataHora(e.criadoEm)}</TableCell>
                      <TableCell>{e.cliente}</TableCell>
                      <TableCell className="text-right tabular-nums">{e.quantidade}</TableCell>
                      <TableCell className="text-right tabular-nums">{formatarCentavos(e.valorCents)}</TableCell>
                      <TableCell>
                        <Badge variant={e.situacao === 'Enviado' ? 'default' : e.situacao === 'Parado' ? 'destructive' : 'secondary'}>{ROTULO[e.situacao]}</Badge>
                        {e.ultimoErro && <p className="mt-1 max-w-64 truncate text-xs text-muted-foreground" title={e.ultimoErro}>{e.tentativas} tentativas: {e.ultimoErro}</p>}
                      </TableCell>
                      <TableCell className="tabular-nums">{e.numeroNoErp ?? '-'}</TableCell>
                      <TableCell className="text-right">
                        {e.situacao === 'Parado' && <Button variant="outline" onClick={() => reenviar.mutate(e.pedidoId)}>Reenviar</Button>}
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
