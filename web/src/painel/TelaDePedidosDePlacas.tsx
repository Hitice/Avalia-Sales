import { useState } from 'react'
import { mensagemDoErro } from '@/api/cliente'
import { formatarDataHora, ROTULO_DO_PEDIDO, rotuloDoPedido, TOM_DO_PEDIDO } from '@/api/rotulos'
import type { SituacaoDoPedido } from '@/api/tipos'
import { AcoesDaLinha } from '@/componentes/AcoesDaLinha'
import { Filtro, mesesRecentes, opcoesDe } from '@/componentes/Filtro'
import { ListaPaginada } from '@/componentes/ListaPaginada'
import { Cabecalho, Erro, Rolagem } from '@/componentes/Tela'
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
  const [situacao, setSituacao] = useState<SituacaoDoPedido>()
  const [mes, setMes] = useState<string>()
  const pedidos = usePedidos({ situacao, mes })
  const cancelar = useCancelarPedido()
  const ativar = useAtivarLote()
  const pagar = usePagarPedido()
  const [numero, setNumero] = useState('')

  return (
    <div className="grid grid-cols-1 gap-4 xl:lista-e-lateral-80">
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
            <ListaPaginada lista={pedidos} textoVazio="Nenhum pedido ainda." nomeDaBusca="Buscar nos pedidos" placeholder="Cupom ou lote"
              filtrada={!!(situacao || mes)}
              filtros={<>
                <Filtro rotulo="Situação" todos="Todas as situações" valor={situacao} aoMudar={setSituacao} opcoes={opcoesDe(ROTULO_DO_PEDIDO)} />
                <Filtro rotulo="Mês" todos="Todos os meses" valor={mes} aoMudar={setMes} opcoes={mesesRecentes()} />
              </>}>
              {(itens) => (
                <Rolagem>
                  <Table semBusca>
                    <TableHeader>
                      <TableRow>
                        <TableHead tipo="data">Pedido em</TableHead>
                        <TableHead tipo="numero">Placas</TableHead>
                        <TableHead tipo="numero">Valor</TableHead>
                        <TableHead tipo="situacao">Situação</TableHead>
                        <TableHead>Lote</TableHead>
                        <TableHead tipo="acoes" />
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {itens.map((p) => (
                        <TableRow key={p.id}>
                          <TableCell>{formatarDataHora(p.criadoEm)}</TableCell>
                          <TableCell>{p.quantidade}</TableCell>
                          <TableCell>
                            {formatarCentavos(p.valorCents)}
                            {p.cupom && <span className="block text-xs text-muted-foreground">cupom {p.cupom}, menos {formatarCentavos(p.descontoCents)}</span>}
                          </TableCell>
                          <TableCell><Badge variant={TOM_DO_PEDIDO[p.situacao]}>{rotuloDoPedido(p)}</Badge></TableCell>
                          <TableCell>
                            {p.lote ?? '-'}
                            {p.transportadora && <span className="block text-xs text-muted-foreground">{p.transportadora}</span>}
                            {p.codigoDeRastreio && <span className="block text-xs text-muted-foreground tabular-nums">Rastreio {p.codigoDeRastreio}</span>}
                          </TableCell>
                          <TableCell>
                            {p.situacao === 'AguardandoPagamento' && (
                              <AcoesDaLinha nome={`pedido de ${p.quantidade} placas`}
                                principal={{
                                  rotulo: 'Pagar',
                                  desabilitada: pagar.isPending,
                                  aoClicar: async () => {
                                    // A página do Pagar.me abre em outra aba; o pedido fica pago aqui quando o pagamento cai.
                                    const aba = window.open('', '_blank')
                                    const url = p.urlDePagamento ?? (await pagar.mutateAsync(p.id).catch(() => null))?.url
                                    if (url && aba) aba.location.href = url
                                    else aba?.close()
                                  },
                                }}
                                outras={[{ rotulo: 'Cancelar', destrutiva: true, desabilitada: cancelar.isPending, aoClicar: () => cancelar.mutate(p.id) }]} />
                            )}
                          </TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </Rolagem>
              )}
            </ListaPaginada>
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
