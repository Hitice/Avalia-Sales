import { useState } from 'react'
import { mensagemDoErro } from '@/api/cliente'
import { formatarBps, formatarData, paraBps, type Tom } from '@/api/rotulos'
import type { Revendedor } from '@/api/tipos'
import { AcoesDaLinha } from '@/componentes/AcoesDaLinha'
import { ListaPaginada } from '@/componentes/ListaPaginada'
import { Cabecalho, Erro, Rolagem } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Field, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { mascaraTelefone } from '@/lib/mascaras'
import { useContratoAssinado, useDefinirRecebedor, useDefinirRenovacao, useReabertura, useRevendedores, useSuspensao } from './consultas'

const formatarCnpj = (c: string | null) => c ? c.replace(/^(\d{2})(\d{3})(\d{3})(\d{4})(\d{2})$/, '$1.$2.$3/$4-$5') : '-'

/** A situação da conta, da mais grave à normal, com o detalhe que vai embaixo do selo. */
function situacaoDaConta(r: Revendedor): { rotulo: string; tom: Tom; detalhe?: string } {
  if (r.encerradaEm && r.reaberturaPedidaEm) return { rotulo: 'Reabertura pedida', tom: 'atencao', detalhe: formatarData(r.reaberturaPedidaEm) }
  if (r.encerradaEm) return { rotulo: 'Encerrada', tom: 'neutro', detalhe: formatarData(r.encerradaEm) }
  if (r.suspensaEm) return { rotulo: 'Suspensa', tom: 'erro', detalhe: r.motivoDaSuspensao ?? undefined }
  if (r.encerramentoPedidoEm) return { rotulo: 'Distrato', tom: 'atencao', detalhe: 'Em assinatura' }
  return { rotulo: 'Ativa', tom: 'sucesso' }
}

/** Cadastro dos revendedores: conta de recebimento no Pagar.me, contrato assinado em papel e reabertura da conta encerrada. */
export function TelaDeRevendedores() {
  const revendedores = useRevendedores()
  const contrato = useContratoAssinado()
  const reabertura = useReabertura()
  const suspensao = useSuspensao()
  const [editando, setEditando] = useState<Revendedor | null>(null)
  const [suspendendo, setSuspendendo] = useState<Revendedor | null>(null)
  const erro = contrato.error ?? reabertura.error ?? suspensao.error

  return (
    <>
      <Cabecalho titulo="Revendedores" />
      <Erro erro={erro && !suspendendo ? mensagemDoErro(erro) : null} />
      <Card className="mt-3">
        <CardContent>
          <ListaPaginada lista={revendedores} textoVazio="Nenhum revendedor ainda." nomeDaBusca="Buscar nos revendedores" placeholder="Nome ou CNPJ">
            {(itens) => (
              <Rolagem>
                <Table semBusca>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Revendedor</TableHead>
                      <TableHead tipo="situacao">Situação</TableHead>
                      <TableHead>CNPJ</TableHead>
                      <TableHead>Celular</TableHead>
                      <TableHead tipo="numero">Taxa</TableHead>
                      <TableHead tipo="numero">Renovação</TableHead>
                      <TableHead tipo="situacao">Contrato</TableHead>
                      <TableHead>Recebedor no Pagar.me</TableHead>
                      <TableHead tipo="acoes" />
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {itens.map((r) => {
                      const conta = situacaoDaConta(r)
                      const ativa = !r.encerradaEm
                      return (
                        <TableRow key={r.id}>
                          <TableCell className="min-w-40 font-medium whitespace-normal">{r.nome}</TableCell>
                          <TableCell>
                            <Badge variant={conta.tom}>{conta.rotulo}</Badge>
                            {conta.detalhe && <span className="text-xs text-muted-foreground max-md:ml-2">{conta.detalhe}</span>}
                          </TableCell>
                          <TableCell className="tabular-nums">{formatarCnpj(r.cnpj)}</TableCell>
                          <TableCell className="tabular-nums">{r.celular ? mascaraTelefone(r.celular) : '-'}</TableCell>
                          <TableCell>{formatarBps(r.taxaAvaliaBps)}</TableCell>
                          <TableCell>{formatarBps(r.renovacaoBps)}</TableCell>
                          <TableCell>
                            <Badge variant={r.contratoAssinadoEm ? 'sucesso' : 'atencao'}>{r.contratoAssinadoEm ? 'Assinado' : 'Sem assinatura'}</Badge>
                            {r.contratoAssinadoEm && <span className="text-xs text-muted-foreground tabular-nums max-md:ml-2">{formatarData(r.contratoAssinadoEm)}</span>}
                          </TableCell>
                          <TableCell className="font-mono text-xs">{r.recebedorPagarme ?? '-'}</TableCell>
                          <TableCell>
                            <AcoesDaLinha nome={r.nome} principal={{ rotulo: 'Editar', aoClicar: () => setEditando(r) }} outras={[
                              ativa && !r.encerramentoPedidoEm && !r.contratoAssinadoEm && { rotulo: 'Registrar contrato assinado', desabilitada: contrato.isPending, aoClicar: () => contrato.mutate(r.id) },
                              !!r.encerradaEm && !!r.reaberturaPedidaEm && { rotulo: 'Aprovar reabertura', desabilitada: reabertura.isPending, aoClicar: () => reabertura.mutate({ id: r.id, aprovar: true }) },
                              !!r.encerradaEm && !!r.reaberturaPedidaEm && { rotulo: 'Recusar reabertura', destrutiva: true, desabilitada: reabertura.isPending, aoClicar: () => reabertura.mutate({ id: r.id, aprovar: false }) },
                              ativa && !!r.suspensaEm && { rotulo: 'Reativar', desabilitada: suspensao.isPending, aoClicar: () => suspensao.mutate({ id: r.id, suspender: false }) },
                              ativa && !r.suspensaEm && { rotulo: 'Suspender', destrutiva: true, aoClicar: () => { suspensao.reset(); setSuspendendo(r) } },
                            ]} />
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
      {editando && <EdicaoDoRevendedor key={editando.id} revendedor={editando} aoFechar={() => setEditando(null)} />}
      {suspendendo && (
        <Suspensao revendedor={suspendendo} ocupado={suspensao.isPending} erro={suspensao.error} aoFechar={() => setSuspendendo(null)}
          aoSuspender={(motivo) => suspensao.mutate({ id: suspendendo.id, suspender: true, motivo }, { onSuccess: () => setSuspendendo(null) })} />
      )}
    </>
  )
}

/** A renovação (em % da venda) e a conta de recebimento no Pagar.me. Só o que mudou vai à API. */
function EdicaoDoRevendedor({ revendedor, aoFechar }: { revendedor: Revendedor; aoFechar: () => void }) {
  const renovacao = useDefinirRenovacao()
  const recebedor = useDefinirRecebedor()
  const renovacaoAtual = String(revendedor.renovacaoBps / 100).replace('.', ',')
  const [percentual, setPercentual] = useState(renovacaoAtual)
  const [conta, setConta] = useState(revendedor.recebedorPagarme ?? '')
  const bps = paraBps(percentual)
  const mudouRenovacao = percentual.trim() !== renovacaoAtual
  const mudouConta = conta.trim() !== (revendedor.recebedorPagarme ?? '') && conta.trim() !== ''
  const erro = renovacao.error ?? recebedor.error

  async function salvar() {
    try {
      if (mudouRenovacao && bps !== null) await renovacao.mutateAsync({ id: revendedor.id, renovacaoBps: bps })
      if (mudouConta) await recebedor.mutateAsync({ id: revendedor.id, recebedor: conta.trim() })
      aoFechar()
    } catch {
      // O erro aparece pelo estado da gravação.
    }
  }

  return (
    <Dialog open onOpenChange={(aberto) => !aberto && aoFechar()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Editar {revendedor.nome}</DialogTitle>
          <DialogDescription>Taxa da Avalia {formatarBps(revendedor.taxaAvaliaBps)}.</DialogDescription>
        </DialogHeader>
        <form className="grid gap-4" onSubmit={(e) => { e.preventDefault(); void salvar() }}>
          <Erro erro={erro ? mensagemDoErro(erro) : null} />
          <Field>
            <FieldLabel htmlFor="renovacao">Renovação (% da venda)</FieldLabel>
            <Input id="renovacao" inputMode="decimal" value={percentual} aria-invalid={mudouRenovacao && bps === null} onChange={(e) => setPercentual(e.target.value)} />
          </Field>
          <Field>
            <FieldLabel htmlFor="recebedor">Recebedor no Pagar.me</FieldLabel>
            <Input id="recebedor" placeholder="rp_... ou re_..." autoComplete="off" value={conta} onChange={(e) => setConta(e.target.value)} />
          </Field>
          <DialogFooter>
            <Button type="button" variant="ghost" onClick={aoFechar}>Cancelar</Button>
            <Button type="submit" disabled={(!mudouRenovacao && !mudouConta) || (mudouRenovacao && bps === null) || renovacao.isPending || recebedor.isPending}>Salvar</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}

/** Suspender deixa a revenda só em consulta; o motivo aparece para ela. */
function Suspensao({ revendedor, ocupado, erro, aoFechar, aoSuspender }: {
  revendedor: Revendedor
  ocupado: boolean
  erro: unknown
  aoFechar: () => void
  aoSuspender: (motivo: string | null) => void
}) {
  const [motivo, setMotivo] = useState('')
  return (
    <Dialog open onOpenChange={(aberto) => !aberto && aoFechar()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Suspender {revendedor.nome}</DialogTitle>
          <DialogDescription>A revenda fica só em consulta até ser reativada.</DialogDescription>
        </DialogHeader>
        <form className="grid gap-4" onSubmit={(e) => { e.preventDefault(); aoSuspender(motivo.trim() || null) }}>
          <Erro erro={erro ? mensagemDoErro(erro) : null} />
          <Field>
            <FieldLabel htmlFor="motivo-da-suspensao">Motivo (opcional)</FieldLabel>
            <Input id="motivo-da-suspensao" maxLength={200} value={motivo} onChange={(e) => setMotivo(e.target.value)} />
          </Field>
          <DialogFooter>
            <Button type="button" variant="ghost" onClick={aoFechar}>Voltar</Button>
            <Button type="submit" variant="destructive" disabled={ocupado}>Suspender</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
