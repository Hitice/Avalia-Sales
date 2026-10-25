import { mensagemDoErro } from '@/api/cliente'
import { formatarDataHora } from '@/api/rotulos'
import { AcoesDaLinha } from '@/componentes/AcoesDaLinha'
import { Cabecalho, Erro, EstadoDaConsulta, Rolagem } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
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
                    <TableHead tipo="data">Pago em</TableHead>
                    <TableHead>Revendedor</TableHead>
                    <TableHead tipo="numero">Placas</TableHead>
                    <TableHead tipo="numero">Valor</TableHead>
                    <TableHead tipo="situacao">Situação</TableHead>
                    <TableHead>Pedido de venda</TableHead>
                    <TableHead tipo="acoes" />
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {fila.data.map((e) => (
                    <TableRow key={e.pedidoId}>
                      <TableCell>{formatarDataHora(e.criadoEm)}</TableCell>
                      <TableCell className="min-w-48 whitespace-normal">
                        {e.cliente}
                        {e.ultimoErro && <p className="text-xs text-muted-foreground">{e.tentativas} tentativas: {e.ultimoErro}</p>}
                      </TableCell>
                      <TableCell>{e.quantidade}</TableCell>
                      <TableCell>{formatarCentavos(e.valorCents)}</TableCell>
                      <TableCell><Badge variant={e.situacao === 'Enviado' ? 'sucesso' : e.situacao === 'Parado' ? 'erro' : 'atencao'}>{ROTULO[e.situacao]}</Badge></TableCell>
                      <TableCell className="tabular-nums">{e.numeroNoErp ?? '-'}</TableCell>
                      <TableCell>
                        <AcoesDaLinha nome={`pedido de ${e.cliente}`} principal={e.situacao === 'Parado' && { rotulo: 'Reenviar', desabilitada: reenviar.isPending, aoClicar: () => reenviar.mutate(e.pedidoId) }} />
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
