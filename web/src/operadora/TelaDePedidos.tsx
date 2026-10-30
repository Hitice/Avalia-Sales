import { Plus } from 'lucide-react'
import { useState } from 'react'
import { mensagemDoErro } from '@/api/cliente'
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
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatarCentavos, paraCentavos } from '@/dinheiro/dinheiro'
import { useAbrirPedidoAvulso, useIniciarProducao, useLotesLivres, usePedidosDaOperadora, useRegistrarPagamento, useRevendasDoPedidoAvulso } from './consultas'
import { useDespacharComEnvio, useEstornarPedido, useFornecedoresDeLogistica } from './consultasDeEntregaEAcerto'

/** A fila da operadora: pedido pago ganha um lote livre da mesma quantidade. */
export function TelaDePedidos() {
  const pedidos = usePedidosDaOperadora()
  const pagar = useRegistrarPagamento()
  const despachar = useDespacharComEnvio()
  const estornar = useEstornarPedido()
  const producao = useIniciarProducao()
  const [despachando, setDespachando] = useState<Pedido | null>(null)
  const [estornando, setEstornando] = useState<Pedido | null>(null)
  const [abrindo, setAbrindo] = useState(false)

  return (
    <>
      <Cabecalho titulo="Pedidos">
        <Button onClick={() => setAbrindo(true)}><Plus aria-hidden />Novo pedido</Button>
      </Cabecalho>
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
                          <TableCell>
                            {pedido.lote ?? (semLote ? <span className="text-muted-foreground">Gere um lote de {pedido.quantidade}</span> : '-')}
                            {pedido.transportadora && <span className="block text-xs text-muted-foreground">{pedido.transportadora}</span>}
                            {pedido.codigoDeRastreio && <span className="block text-xs text-muted-foreground tabular-nums">Rastreio {pedido.codigoDeRastreio}</span>}
                          </TableCell>
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
                                aDespachar && { rotulo: 'Estornar pedido', destrutiva: true, aoClicar: () => { estornar.reset(); setEstornando(pedido) } },
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
          aoDespachar={(dados) => despachar.mutate({ id: despachando.id, ...dados }, { onSuccess: () => setDespachando(null) })} />
      )}
      {abrindo && <PedidoAvulso aoFechar={() => setAbrindo(false)} />}
      {estornando && (
        <Estorno pedido={estornando} ocupado={estornar.isPending} erro={estornar.error} aoFechar={() => setEstornando(null)}
          aoEstornar={(motivo) => estornar.mutate({ id: estornando.id, motivo }, { onSuccess: () => setEstornando(null) })} />
      )}
    </>
  )
}

/** Fora dos pacotes, para a placa avulsa ou a reposição: a revenda paga em Pedidos, e o valor zero já nasce pago. */
function PedidoAvulso({ aoFechar }: { aoFechar: () => void }) {
  const revendas = useRevendasDoPedidoAvulso()
  const abrir = useAbrirPedidoAvulso()
  const [revendedorId, setRevendedorId] = useState('')
  const [quantidade, setQuantidade] = useState('1')
  const [valor, setValor] = useState('0,00')
  const placas = Number(quantidade)
  const cents = paraCentavos(valor)
  const valido = revendedorId !== '' && Number.isInteger(placas) && placas >= 1 && placas <= 1000 && cents !== null
  return (
    <Dialog open onOpenChange={(aberto) => !aberto && aoFechar()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Novo pedido</DialogTitle>
          <DialogDescription>Fora dos pacotes. Valor zero já nasce pago.</DialogDescription>
        </DialogHeader>
        <form
          className="grid gap-4"
          onSubmit={(e) => {
            e.preventDefault()
            if (!valido) return
            abrir.mutate({ revendedorId, quantidade: placas, valorCents: cents }, { onSuccess: aoFechar })
          }}
        >
          <Erro erro={abrir.error ?? revendas.error} />
          <Field>
            <FieldLabel htmlFor="revenda-do-pedido">Revenda</FieldLabel>
            <Select value={revendedorId} onValueChange={setRevendedorId}>
              <SelectTrigger id="revenda-do-pedido" className="w-full"><SelectValue placeholder="Escolha" /></SelectTrigger>
              <SelectContent>
                {(revendas.data ?? []).map((r) => <SelectItem key={r.id} value={r.id}>{r.nome}</SelectItem>)}
              </SelectContent>
            </Select>
          </Field>
          <Field>
            <FieldLabel htmlFor="quantidade-do-pedido">Placas</FieldLabel>
            <Input id="quantidade-do-pedido" type="number" inputMode="numeric" min={1} max={1000} value={quantidade} onChange={(e) => setQuantidade(e.target.value)} />
          </Field>
          <Field>
            <FieldLabel htmlFor="valor-do-pedido">Valor</FieldLabel>
            <Input id="valor-do-pedido" inputMode="decimal" autoComplete="off" value={valor} aria-invalid={cents === null} onChange={(e) => setValor(e.target.value)} />
          </Field>
          <DialogFooter>
            <Button type="button" variant="ghost" onClick={aoFechar}>Cancelar</Button>
            <Button type="submit" disabled={!valido || abrir.isPending}>Abrir pedido</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}

const OUTRA = '__outra__'

/** O lote livre da mesma quantidade vai para a revenda: enviado pelo correio ou pronto para ela retirar. */
function Despacho({ pedido, ocupado, erro, aoFechar, aoDespachar }: {
  pedido: Pedido
  ocupado: boolean
  erro: unknown
  aoFechar: () => void
  aoDespachar: (dados: { lote: number; retirada: boolean; transportadora: string | null; codigoDeRastreio: string | null }) => void
}) {
  const livres = useLotesLivres(pedido.quantidade)
  const lotes = livres.data?.itens ?? []
  const [escolhido, setLote] = useState('')
  // Com um lote só, ele já vem escolhido.
  const lote = escolhido || (lotes.length === 1 ? String(lotes[0].numero) : '')
  const fornecedores = useFornecedoresDeLogistica()
  const nomes = (fornecedores.data?.itens ?? []).map((f) => f.nome)
  const [transportadora, setTransportadora] = useState('')
  const [outra, setOutra] = useState(false)
  const [rastreio, setRastreio] = useState('')
  const enviar = (retirada: boolean) => aoDespachar({
    lote: Number(lote),
    retirada,
    transportadora: retirada ? null : transportadora.trim() || null,
    codigoDeRastreio: retirada ? null : rastreio.trim() || null,
  })
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
        <Field>
          <FieldLabel htmlFor="transportadora-do-despacho">Transportadora</FieldLabel>
          {nomes.length > 0 && !outra ? (
            <Select value={transportadora} onValueChange={(v) => (v === OUTRA ? (setOutra(true), setTransportadora('')) : setTransportadora(v))}>
              <SelectTrigger id="transportadora-do-despacho" className="w-full"><SelectValue placeholder="Escolha" /></SelectTrigger>
              <SelectContent>
                {nomes.map((n) => <SelectItem key={n} value={n}>{n}</SelectItem>)}
                <SelectItem value={OUTRA}>Outra</SelectItem>
              </SelectContent>
            </Select>
          ) : (
            <Input id="transportadora-do-despacho" maxLength={100} autoComplete="off" value={transportadora} onChange={(e) => setTransportadora(e.target.value)} />
          )}
        </Field>
        <Field>
          <FieldLabel htmlFor="rastreio-do-despacho">Código de rastreio</FieldLabel>
          <Input id="rastreio-do-despacho" maxLength={100} autoComplete="off" value={rastreio} onChange={(e) => setRastreio(e.target.value)} />
        </Field>
        <DialogFooter>
          <Button variant="outline" disabled={!lote || ocupado} onClick={() => enviar(true)}>Pronto para retirada</Button>
          <Button disabled={!lote || ocupado} onClick={() => enviar(false)}>Despachar</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

/** O estorno muda a situação e avisa a revenda; o dinheiro volta por fora do sistema. */
function Estorno({ pedido, ocupado, erro, aoFechar, aoEstornar }: {
  pedido: Pedido
  ocupado: boolean
  erro: unknown
  aoFechar: () => void
  aoEstornar: (motivo: string | null) => void
}) {
  const [motivo, setMotivo] = useState('')
  return (
    <Dialog open onOpenChange={(aberto) => !aberto && aoFechar()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Estornar pedido</DialogTitle>
          <DialogDescription>{pedido.revendedor}, {pedido.quantidade} placas. A devolução do dinheiro sai pelo painel do Pagar.me ou por fora.</DialogDescription>
        </DialogHeader>
        <form className="grid gap-4" onSubmit={(e) => { e.preventDefault(); aoEstornar(motivo.trim() || null) }}>
          <Erro erro={erro ? mensagemDoErro(erro) : null} />
          <Field>
            <FieldLabel htmlFor="motivo-do-estorno">Motivo (opcional)</FieldLabel>
            <Input id="motivo-do-estorno" maxLength={200} value={motivo} onChange={(e) => setMotivo(e.target.value)} />
          </Field>
          <DialogFooter>
            <Button type="button" variant="ghost" onClick={aoFechar}>Voltar</Button>
            <Button type="submit" variant="destructive" disabled={ocupado}>Estornar pedido</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
