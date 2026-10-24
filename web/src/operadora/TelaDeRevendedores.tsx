import { useState } from 'react'
import { mensagemDoErro } from '@/api/cliente'
import { formatarBps, formatarData } from '@/api/rotulos'
import { Cabecalho, Erro, EstadoDaConsulta, Rolagem } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { mascaraTelefone } from '@/lib/mascaras'
import { useContratoAssinado, useDefinirRecebedor, useReabertura, useRevendedores, useSuspensao } from './consultas'

const formatarCnpj = (c: string | null) => c ? c.replace(/^(\d{2})(\d{3})(\d{3})(\d{4})(\d{2})$/, '$1.$2.$3/$4-$5') : '-'

/** Cadastro dos revendedores: conta de recebimento no Pagar.me, contrato assinado em papel e reabertura da conta encerrada. */
export function TelaDeRevendedores() {
  const revendedores = useRevendedores()
  const recebedor = useDefinirRecebedor()
  const contrato = useContratoAssinado()
  const reabertura = useReabertura()
  const suspensao = useSuspensao()
  const [suspendendo, setSuspendendo] = useState<string | null>(null)
  const [motivo, setMotivo] = useState('')
  const [digitado, setDigitado] = useState<Record<string, string>>({})

  return (
    <>
      <Cabecalho titulo="Revendedores" />
      <Erro erro={recebedor.error || contrato.error || reabertura.error ? mensagemDoErro(recebedor.error ?? contrato.error ?? reabertura.error) : null} />
      <Card className="mt-3">
        <CardContent>
          <EstadoDaConsulta carregando={revendedores.isPending} erro={revendedores.error} vazio={revendedores.data?.length === 0} textoVazio="Nenhum revendedor ainda." />
          {revendedores.data && revendedores.data.length > 0 && (
            <Rolagem>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Revendedor</TableHead>
                    <TableHead>CNPJ</TableHead>
                    <TableHead>Celular</TableHead>
                    <TableHead className="text-right">Taxa</TableHead>
                    <TableHead>Contrato</TableHead>
                    <TableHead>Recebedor no Pagar.me</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {revendedores.data.map((r) => (
                    <TableRow key={r.id}>
                                      <TableCell className="min-w-32 font-medium whitespace-normal">
                        <span className="block">{r.nome}</span>
                        {!r.encerradaEm && (r.suspensaEm
                          ? (
                              <span className="flex flex-wrap items-center gap-2">
                                <Badge variant="destructive">Suspensa{r.motivoDaSuspensao ? `: ${r.motivoDaSuspensao}` : ''}</Badge>
                                <Button variant="ghost" size="sm" disabled={suspensao.isPending} onClick={() => suspensao.mutate({ id: r.id, suspender: false })}>Reativar</Button>
                              </span>
                            )
                          : suspendendo === r.id
                            ? (
                                <form className="flex gap-2" onSubmit={(e) => {
                                  e.preventDefault()
                                  suspensao.mutate({ id: r.id, suspender: true, motivo: motivo.trim() || null }, { onSuccess: () => { setSuspendendo(null); setMotivo('') } })
                                }}>
                                  <Input aria-label="Motivo da suspensão" maxLength={200} placeholder="Motivo (opcional)" value={motivo} onChange={(e) => setMotivo(e.target.value)} />
                                  <Button type="submit" variant="destructive" size="sm" disabled={suspensao.isPending}>Suspender</Button>
                                  <Button type="button" variant="ghost" size="sm" onClick={() => setSuspendendo(null)}>Voltar</Button>
                                </form>
                              )
                            : <Button variant="ghost" size="sm" className="px-0 text-destructive" onClick={() => setSuspendendo(r.id)}>Suspender</Button>)}
                      </TableCell>
                      <TableCell className="tabular-nums">{formatarCnpj(r.cnpj)}</TableCell>
                      <TableCell className="tabular-nums">{r.celular ? mascaraTelefone(r.celular) : '-'}</TableCell>
                      <TableCell className="text-right tabular-nums">{formatarBps(r.taxaAvaliaBps)}</TableCell>
                      <TableCell>
                        {r.encerradaEm && r.reaberturaPedidaEm
                          ? (
                              <div className="flex flex-wrap items-center gap-2">
                                <Badge variant="secondary">Reabertura pedida em {formatarData(r.reaberturaPedidaEm)}</Badge>
                                <Button variant="outline" size="sm" disabled={reabertura.isPending} onClick={() => reabertura.mutate({ id: r.id, aprovar: true })}>Aprovar</Button>
                                <Button variant="ghost" size="sm" disabled={reabertura.isPending} onClick={() => reabertura.mutate({ id: r.id, aprovar: false })}>Recusar</Button>
                              </div>
                            )
                          : r.encerradaEm
                          ? <Badge variant="outline">Encerrada em {formatarData(r.encerradaEm)}</Badge>
                          : r.encerramentoPedidoEm
                            ? <Badge variant="secondary">Distrato em assinatura</Badge>
                            : r.contratoAssinadoEm
                              ? <Badge>Assinado em {formatarData(r.contratoAssinadoEm)}</Badge>
                              : <Button variant="outline" onClick={() => contrato.mutate(r.id)}>Registrar assinado</Button>}
                      </TableCell>
                      <TableCell>
                        <form
                          className="flex gap-2"
                          onSubmit={(e) => {
                            e.preventDefault()
                            recebedor.mutate({ id: r.id, recebedor: digitado[r.id] ?? '' })
                          }}
                        >
                          <Input
                            aria-label={`Recebedor de ${r.nome}`}
                            className="w-40"
                            placeholder="rp_... ou re_..."
                            defaultValue={r.recebedorPagarme ?? ''}
                            onChange={(e) => setDigitado({ ...digitado, [r.id]: e.target.value })}
                          />
                          <Button type="submit" variant="outline" disabled={!digitado[r.id]}>Salvar</Button>
                        </form>
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
