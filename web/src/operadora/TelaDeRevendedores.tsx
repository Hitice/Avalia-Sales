import { useState } from 'react'
import { mensagemDoErro } from '@/api/cliente'
import { formatarBps, formatarData, paraBps } from '@/api/rotulos'
import { Cabecalho, Erro, EstadoDaConsulta, Rolagem } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { mascaraTelefone } from '@/lib/mascaras'
import { useContratoAssinado, useDefinirRecebedor, useDefinirRenovacao, useReabertura, useRevendedores, useSuspensao } from './consultas'

const formatarCnpj = (c: string | null) => c ? c.replace(/^(\d{2})(\d{3})(\d{3})(\d{4})(\d{2})$/, '$1.$2.$3/$4-$5') : '-'

/** Cadastro dos revendedores: conta de recebimento no Pagar.me, contrato assinado em papel e reabertura da conta encerrada. */
export function TelaDeRevendedores() {
  const revendedores = useRevendedores()
  const recebedor = useDefinirRecebedor()
  const renovacao = useDefinirRenovacao()
  const [renovacoes, setRenovacoes] = useState<Record<string, string>>({})
  const contrato = useContratoAssinado()
  const reabertura = useReabertura()
  const suspensao = useSuspensao()
  const [suspendendo, setSuspendendo] = useState<string | null>(null)
  const [motivo, setMotivo] = useState('')
  const [digitado, setDigitado] = useState<Record<string, string>>({})

  return (
    <>
      <Cabecalho titulo="Revendedores" />
      <Erro erro={recebedor.error || renovacao.error || contrato.error || reabertura.error ? mensagemDoErro(recebedor.error ?? renovacao.error ?? contrato.error ?? reabertura.error) : null} />
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
                    <TableHead>Renovação (% da venda)</TableHead>
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
                                <Badge variant="erro">Suspensa{r.motivoDaSuspensao ? `: ${r.motivoDaSuspensao}` : ''}</Badge>
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
                            : <Button variant="ghost" size="sm" className="px-0 text-erro hover:bg-transparent hover:text-erro hover:underline" onClick={() => setSuspendendo(r.id)}>Suspender</Button>)}
                      </TableCell>
                      <TableCell className="tabular-nums">{formatarCnpj(r.cnpj)}</TableCell>
                      <TableCell className="tabular-nums">{r.celular ? mascaraTelefone(r.celular) : '-'}</TableCell>
                      <TableCell className="text-right tabular-nums">{formatarBps(r.taxaAvaliaBps)}</TableCell>
                      <TableCell>
                        <form
                          className="flex gap-2"
                          onSubmit={(e) => {
                            e.preventDefault()
                            const bps = paraBps(renovacoes[r.id] ?? '')
                            if (bps !== null) renovacao.mutate({ id: r.id, renovacaoBps: bps })
                          }}
                        >
                          <Input
                            aria-label={`Renovação de ${r.nome}, em porcentagem da venda`}
                            className="w-20"
                            inputMode="decimal"
                            defaultValue={String(r.renovacaoBps / 100).replace('.', ',')}
                            onChange={(e) => setRenovacoes({ ...renovacoes, [r.id]: e.target.value })}
                          />
                          <Button type="submit" variant="outline" disabled={paraBps(renovacoes[r.id] ?? '') === null || renovacao.isPending}>Salvar</Button>
                        </form>
                      </TableCell>
                      <TableCell>
                        {r.encerradaEm && r.reaberturaPedidaEm
                          ? (
                              <div className="flex flex-wrap items-center gap-2">
                                <Badge variant="atencao">Reabertura pedida em {formatarData(r.reaberturaPedidaEm)}</Badge>
                                <Button variant="outline" size="sm" disabled={reabertura.isPending} onClick={() => reabertura.mutate({ id: r.id, aprovar: true })}>Aprovar</Button>
                                <Button variant="ghost" size="sm" disabled={reabertura.isPending} onClick={() => reabertura.mutate({ id: r.id, aprovar: false })}>Recusar</Button>
                              </div>
                            )
                          : r.encerradaEm
                          ? <Badge variant="neutro">Encerrada em {formatarData(r.encerradaEm)}</Badge>
                          : r.encerramentoPedidoEm
                            ? <Badge variant="atencao">Distrato em assinatura</Badge>
                            : r.contratoAssinadoEm
                              ? <Badge variant="sucesso">Assinado em {formatarData(r.contratoAssinadoEm)}</Badge>
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
