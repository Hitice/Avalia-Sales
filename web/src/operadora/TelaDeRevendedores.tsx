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
import { digitos, mascaraCnpj, mascaraTelefone } from '@/lib/mascaras'
import { useContratoAssinado, useDefinirGrupo, useDefinirRecebedor, useDefinirRenovacao, useReabertura, useRevendedores, useSuspensao } from './consultas'
import { useCadastroDoRevendedor, useLiberarDestinos } from './consultasDeEntregaEAcerto'

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
  const grupo = useDefinirGrupo()
  const [grupoDe, setGrupoDe] = useState<Revendedor | null>(null)
  const [editando, setEditando] = useState<Revendedor | null>(null)
  const [suspendendo, setSuspendendo] = useState<Revendedor | null>(null)
  const [liberando, setLiberando] = useState<Revendedor | null>(null)
  const erro = contrato.error ?? reabertura.error ?? suspensao.error ?? (grupoDe ? null : grupo.error)

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
                      <TableHead>Assinado em</TableHead>
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
                          <TableCell className="min-w-40 font-medium whitespace-normal">
                            <span className="flex flex-wrap items-center gap-1.5">
                              {r.nome}
                              {r.doGrupoDaOperadora && <Badge variant="info">Do grupo</Badge>}
                            </span>
                          </TableCell>
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
                          </TableCell>
                          <TableCell className="tabular-nums">{r.contratoAssinadoEm ? formatarData(r.contratoAssinadoEm) : '-'}</TableCell>
                          <TableCell className="font-mono text-xs">{r.recebedorPagarme ?? '-'}</TableCell>
                          <TableCell>
                            <AcoesDaLinha nome={r.nome} principal={{ rotulo: 'Editar', aoClicar: () => setEditando(r) }} outras={[
                              ativa && !r.encerramentoPedidoEm && !r.contratoAssinadoEm && { rotulo: 'Registrar contrato assinado', desabilitada: contrato.isPending, aoClicar: () => contrato.mutate(r.id) },
                              !!r.encerradaEm && !!r.reaberturaPedidaEm && { rotulo: 'Aprovar reabertura', desabilitada: reabertura.isPending, aoClicar: () => reabertura.mutate({ id: r.id, aprovar: true }) },
                              !!r.encerradaEm && !!r.reaberturaPedidaEm && { rotulo: 'Recusar reabertura', destrutiva: true, desabilitada: reabertura.isPending, aoClicar: () => reabertura.mutate({ id: r.id, aprovar: false }) },
                              ativa && { rotulo: r.doGrupoDaOperadora ? 'Tirar do grupo da operadora' : 'Marcar como do grupo da operadora', aoClicar: () => { grupo.reset(); setGrupoDe(r) } },
                              ativa && { rotulo: 'Liberar cadastros', aoClicar: () => setLiberando(r) },
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
      {grupoDe && (
        <MarcaDoGrupo revendedor={grupoDe} ocupado={grupo.isPending} erro={grupo.error} aoFechar={() => setGrupoDe(null)}
          aoConfirmar={() => grupo.mutate({ id: grupoDe.id, doGrupo: !grupoDe.doGrupoDaOperadora }, { onSuccess: () => setGrupoDe(null) })} />
      )}
      {editando && <EdicaoDoRevendedor key={editando.id} revendedor={editando} aoFechar={() => setEditando(null)} />}
      {liberando && <LiberacaoDeCadastros key={liberando.id} revendedor={liberando} aoFechar={() => setLiberando(null)} />}
      {suspendendo && (
        <Suspensao revendedor={suspendendo} ocupado={suspensao.isPending} erro={suspensao.error} aoFechar={() => setSuspendendo(null)}
          aoSuspender={(motivo) => suspensao.mutate({ id: suspendendo.id, suspender: true, motivo }, { onSuccess: () => setSuspendendo(null) })} />
      )}
    </>
  )
}

/** Nome, CNPJ, renovação (em % da venda) e a conta de recebimento no Pagar.me. Só o que mudou vai à API. */
function EdicaoDoRevendedor({ revendedor, aoFechar }: { revendedor: Revendedor; aoFechar: () => void }) {
  const renovacao = useDefinirRenovacao()
  const recebedor = useDefinirRecebedor()
  const cadastro = useCadastroDoRevendedor()
  const renovacaoAtual = String(revendedor.renovacaoBps / 100).replace('.', ',')
  const [percentual, setPercentual] = useState(renovacaoAtual)
  const [conta, setConta] = useState(revendedor.recebedorPagarme ?? '')
  const [nome, setNome] = useState(revendedor.nome)
  const [cnpj, setCnpj] = useState(mascaraCnpj(revendedor.cnpj ?? ''))
  const bps = paraBps(percentual)
  const mudouCadastro = nome.trim() !== revendedor.nome || digitos(cnpj) !== (revendedor.cnpj ?? '')
  const cadastroValido = nome.trim() !== '' && digitos(cnpj).length === 14
  const mudouRenovacao = percentual.trim() !== renovacaoAtual
  const mudouConta = conta.trim() !== (revendedor.recebedorPagarme ?? '') && conta.trim() !== ''
  const erro = cadastro.error ?? renovacao.error ?? recebedor.error

  async function salvar() {
    try {
      if (mudouCadastro) await cadastro.mutateAsync({ id: revendedor.id, nome: nome.trim(), cnpj: digitos(cnpj) })
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
            <FieldLabel htmlFor="nome-do-revendedor">Nome</FieldLabel>
            <Input id="nome-do-revendedor" maxLength={120} autoComplete="off" value={nome} onChange={(e) => setNome(e.target.value)} />
          </Field>
          <Field>
            <FieldLabel htmlFor="cnpj-do-revendedor">CNPJ</FieldLabel>
            <Input id="cnpj-do-revendedor" inputMode="numeric" autoComplete="off" value={cnpj} aria-invalid={mudouCadastro && !cadastroValido} onChange={(e) => setCnpj(mascaraCnpj(e.target.value))} />
          </Field>
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
            <Button type="submit" disabled={(!mudouCadastro && !mudouRenovacao && !mudouConta) || (mudouCadastro && !cadastroValido) || (mudouRenovacao && bps === null) || cadastro.isPending || renovacao.isPending || recebedor.isPending}>Salvar</Button>
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

/**
 * Marcar a revenda como do grupo da operadora é decisão explícita (não sai do CNPJ nem do nome): a operadora passa a ler
 * os números da revenda, só somados, no resultado do grupo. A marca vale para todos os meses e fica na trilha da operadora.
 */
function MarcaDoGrupo({ revendedor, ocupado, erro, aoFechar, aoConfirmar }: {
  revendedor: Revendedor
  ocupado: boolean
  erro: unknown
  aoFechar: () => void
  aoConfirmar: () => void
}) {
  const tirar = revendedor.doGrupoDaOperadora
  return (
    <Dialog open onOpenChange={(aberto) => !aberto && aoFechar()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{tirar ? 'Tirar' : 'Marcar'} {revendedor.nome} {tirar ? 'do' : 'como do'} grupo da operadora</DialogTitle>
          <DialogDescription>
            {tirar
              ? 'O resultado desta revenda sai do consolidado do grupo, em todos os meses.'
              : 'Use só para a revenda da mesma empresa da operadora. O resultado dela entra no consolidado do grupo, em todos os meses, e a operadora passa a ler os números dela somados. A marca fica registrada com quem fez e quando.'}
          </DialogDescription>
        </DialogHeader>
        <Erro erro={erro ? mensagemDoErro(erro) : null} />
        <DialogFooter>
          <Button type="button" variant="ghost" onClick={aoFechar}>Voltar</Button>
          <Button type="button" disabled={ocupado} onClick={aoConfirmar}>{tirar ? 'Tirar do grupo' : 'Marcar como do grupo'}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

/** Libera mais cadastros de destino de QR e de links só para hoje; o limite diário da revenda volta amanhã. */
function LiberacaoDeCadastros({ revendedor, aoFechar }: { revendedor: Revendedor; aoFechar: () => void }) {
  const liberar = useLiberarDestinos()
  const [quantidade, setQuantidade] = useState('10')
  const n = Number(quantidade)
  const valida = /^\d+$/.test(quantidade) && n >= 1 && n <= 500
  return (
    <Dialog open onOpenChange={(aberto) => !aberto && aoFechar()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Liberar cadastros de {revendedor.nome}</DialogTitle>
          <DialogDescription>Cadastros de destino de QR e de links a mais, só para hoje.</DialogDescription>
        </DialogHeader>
        <form className="grid gap-4" onSubmit={(e) => { e.preventDefault(); if (valida) liberar.mutate({ id: revendedor.id, quantidade: n }, { onSuccess: aoFechar }) }}>
          <Erro erro={liberar.error ? mensagemDoErro(liberar.error) : null} />
          <Field>
            <FieldLabel htmlFor="quantidade-liberada">Quantidade (1 a 500)</FieldLabel>
            <Input id="quantidade-liberada" inputMode="numeric" autoComplete="off" value={quantidade} aria-invalid={!valida} onChange={(e) => setQuantidade(e.target.value)} />
          </Field>
          <DialogFooter>
            <Button type="button" variant="ghost" onClick={aoFechar}>Cancelar</Button>
            <Button type="submit" disabled={!valida || liberar.isPending}>Liberar</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
