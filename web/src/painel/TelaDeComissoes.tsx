import { useState } from 'react'
import { mensagemDoErro } from '@/api/cliente'
import { formatarData, formatarDataHora } from '@/api/rotulos'
import type { SaldoDeComissao } from '@/api/tipos'
import { Cabecalho, Erro, EstadoDaConsulta, Rolagem } from '@/componentes/Tela'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatarCentavos } from '@/dinheiro/dinheiro'
import { useDesfazerRepasse, useRepassar, useRepasses, useSaldosDeComissao } from './consultas'

/**
 * O saldo de comissão de cada vendedor e o registro do Pix feito a ele. A
 * comissão só entra quando o cliente paga; estorno depois do repasse vira
 * saldo negativo, descontado no próximo.
 */
export function TelaDeComissoes() {
  const saldos = useSaldosDeComissao()
  const repasses = useRepasses()
  const desfazer = useDesfazerRepasse()
  // Só o último de cada vendedor se desfaz: o seguinte foi calculado contando com ele.
  // A lista vem do mais recente: o primeiro não desfeito de cada vendedor é o último dele.
  const ultimos = new Set<string>()
  const vistos = new Set<string>()
  for (const r of repasses.data ?? []) {
    if (r.desfeitoEm || vistos.has(r.vendedorId)) continue
    vistos.add(r.vendedorId)
    ultimos.add(r.id)
  }
  const repassar = useRepassar()
  const [escolhido, setEscolhido] = useState<SaldoDeComissao | null>(null)
  const [ate, setAte] = useState('')
  const [comprovante, setComprovante] = useState('')

  return (
    <div className={`grid grid-cols-1 gap-4 ${escolhido ? 'xl:lista-e-lateral-80' : ''}`}>
      <div className="grid gap-4">
        <Cabecalho titulo="Comissões" />
        <Card>
          <CardContent>
            <EstadoDaConsulta carregando={saldos.isPending} erro={saldos.error} vazio={saldos.data?.length === 0} textoVazio="Nenhum vendedor na equipe." />
            {saldos.data && saldos.data.length > 0 && (
              <Rolagem>
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Vendedor</TableHead>
                      <TableHead className="text-right">Devida</TableHead>
                      <TableHead className="text-right">Repassada</TableHead>
                      <TableHead className="text-right">A pagar</TableHead>
                      <TableHead>Último repasse</TableHead>
                      <TableHead><span className="sr-only">Ação</span></TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {saldos.data.map((s) => (
                      <TableRow key={s.vendedorId}>
                        <TableCell><p className="font-medium">{s.vendedor}</p>{s.chavePix && <p className="text-xs text-muted-foreground">Pix {s.chavePix}</p>}</TableCell>
                        <TableCell className="text-right tabular-nums">{formatarCentavos(s.devidaCents)}</TableCell>
                        <TableCell className="text-right tabular-nums">{formatarCentavos(s.repassadaCents)}</TableCell>
                        <TableCell className={`text-right font-medium tabular-nums ${s.abertaCents < 0 ? 'text-destructive' : ''}`}>{formatarCentavos(s.abertaCents)}</TableCell>
                        <TableCell>{formatarData(s.ultimoRepasse)}</TableCell>
                        <TableCell className="text-right">
                          {s.abertaCents > 0 && <Button variant="outline" onClick={() => { setEscolhido(s); repassar.reset() }}>Registrar repasse</Button>}
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </Rolagem>
            )}
          </CardContent>
        </Card>
        <Card>
          <CardHeader><CardTitle>Repasses feitos</CardTitle></CardHeader>
          <CardContent>
            <EstadoDaConsulta carregando={repasses.isPending} erro={repasses.error} vazio={repasses.data?.length === 0} textoVazio="Nenhum repasse registrado." />
            {repasses.data && repasses.data.length > 0 && (
              <Rolagem>
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Pago em</TableHead>
                      <TableHead>Vendedor</TableHead>
                      <TableHead className="text-right">Valor</TableHead>
                      <TableHead className="text-right">Vendas</TableHead>
                      <TableHead>Até as vendas de</TableHead>
                      <TableHead>Comprovante</TableHead>
                      <TableHead><span className="sr-only">Ações</span></TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {repasses.data.map((r) => (
                      <TableRow key={r.id}>
                        <TableCell>{formatarDataHora(r.pagoEm)}</TableCell>
                        <TableCell>{r.vendedor}</TableCell>
                        <TableCell className="text-right tabular-nums">{formatarCentavos(r.valorCents)}</TableCell>
                        <TableCell className="text-right tabular-nums">{r.vendas}</TableCell>
                        <TableCell>{formatarData(r.ateVendaEm)}</TableCell>
                        <TableCell>{r.comprovante ?? '-'}</TableCell>
                        <TableCell className="text-right">
                          {r.desfeitoEm
                            ? <span className="text-sm text-muted-foreground">Desfeito</span>
                            : ultimos.has(r.id) && <Button variant="ghost" size="sm" disabled={desfazer.isPending} onClick={() => desfazer.mutate(r.id)}>Desfazer</Button>}
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
      {escolhido && (
        <Card className="self-start">
          <CardHeader><CardTitle>Repasse a {escolhido.vendedor}</CardTitle></CardHeader>
          <CardContent>
            <form
              className="grid gap-4"
              onSubmit={(e) => {
                e.preventDefault()
                repassar.mutate(
                  { vendedorId: escolhido.vendedorId, ate: ate ? new Date(`${ate}T23:59:59`).toISOString() : null, comprovante: comprovante || null },
                  { onSuccess: () => { setAte(''); setComprovante('') } },
                )
              }}
            >
              <Erro erro={repassar.error ? mensagemDoErro(repassar.error) : null} />
              {repassar.data && <Alert><AlertDescription>Repasse de {formatarCentavos(repassar.data.valorCents)} registrado, {repassar.data.vendas} vendas.</AlertDescription></Alert>}
              <p className="text-sm">A pagar: <span className="font-medium tabular-nums">{formatarCentavos(escolhido.abertaCents)}</span>.</p>
              <Field>
                <FieldLabel htmlFor="ate">Vendas pagas até</FieldLabel>
                <Input id="ate" type="date" value={ate} onChange={(e) => setAte(e.target.value)} />
              </Field>
              <Field>
                <FieldLabel htmlFor="comprovante">Comprovante do Pix</FieldLabel>
                <Input id="comprovante" placeholder="Id da transação" value={comprovante} onChange={(e) => setComprovante(e.target.value)} />
              </Field>
              <Button type="submit" disabled={repassar.isPending}>Registrar repasse</Button>
              <Button type="button" variant="ghost" onClick={() => setEscolhido(null)}>Fechar</Button>
            </form>
          </CardContent>
        </Card>
      )}
    </div>
  )
}
