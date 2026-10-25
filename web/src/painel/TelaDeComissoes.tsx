import { useState } from 'react'
import { Link } from 'react-router'
import { mensagemDoErro } from '@/api/cliente'
import { formatarData, formatarDataHora } from '@/api/rotulos'
import type { SaldoDeComissao } from '@/api/tipos'
import { AcoesDaLinha } from '@/componentes/AcoesDaLinha'
import { Cabecalho, Erro, EstadoDaConsulta, Rolagem } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatarCentavos } from '@/dinheiro/dinheiro'
import { fimDoDiaEmBrasilia } from '@/lib/data'
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
  const aPagar = (saldos.data ?? []).filter((s) => s.abertaCents > 0)

  return (
    <div className="grid gap-4">
        <Cabecalho titulo="Comissões">
          <Button disabled={!aPagar.length} title={aPagar.length ? undefined : 'Nenhum vendedor com comissão a pagar'} onClick={() => { setEscolhido(aPagar[0]); repassar.reset() }}>Registrar repasse</Button>
        </Cabecalho>
        {saldos.data && saldos.data.length > 0 && !aPagar.length && <p className="text-sm text-muted-foreground">Nada a pagar agora: a comissão entra quando o cliente paga a venda.</p>}
        <Card>
          <CardContent>
            <EstadoDaConsulta carregando={saldos.isPending} erro={saldos.error} vazio={saldos.data?.length === 0} textoVazio="Nenhum vendedor na equipe."
              acao={<Button asChild variant="outline"><Link to="/painel/equipe">Cadastrar vendedor</Link></Button>} />
            {saldos.data && saldos.data.length > 0 && (
              <Rolagem>
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Vendedor</TableHead>
                      <TableHead tipo="numero">Devida</TableHead>
                      <TableHead tipo="numero">Repassada</TableHead>
                      <TableHead tipo="numero">A pagar</TableHead>
                      <TableHead tipo="data">Último repasse</TableHead>
                      <TableHead tipo="acoes" />
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {saldos.data.map((s) => (
                      <TableRow key={s.vendedorId}>
                        <TableCell className="min-w-48 whitespace-normal"><p className="font-medium">{s.vendedor}</p>{s.chavePix && <p className="text-xs text-muted-foreground">Pix {s.chavePix}</p>}</TableCell>
                        <TableCell>{formatarCentavos(s.devidaCents)}</TableCell>
                        <TableCell>{formatarCentavos(s.repassadaCents)}</TableCell>
                        <TableCell className={`font-medium ${s.abertaCents < 0 ? 'text-destructive' : ''}`}>{formatarCentavos(s.abertaCents)}</TableCell>
                        <TableCell>{formatarData(s.ultimoRepasse)}</TableCell>
                        <TableCell>
                          <AcoesDaLinha nome={s.vendedor} principal={s.abertaCents > 0 && { rotulo: 'Repassar', aoClicar: () => { setEscolhido(s); repassar.reset() } }} />
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
                      <TableHead tipo="data">Pago em</TableHead>
                      <TableHead>Vendedor</TableHead>
                      <TableHead tipo="numero">Valor</TableHead>
                      <TableHead tipo="numero">Vendas</TableHead>
                      <TableHead tipo="data">Até as vendas de</TableHead>
                      <TableHead>Comprovante</TableHead>
                      <TableHead tipo="situacao">Situação</TableHead>
                      <TableHead tipo="acoes" />
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {repasses.data.map((r) => (
                      <TableRow key={r.id}>
                        <TableCell>{formatarDataHora(r.pagoEm)}</TableCell>
                        <TableCell>{r.vendedor}</TableCell>
                        <TableCell>{formatarCentavos(r.valorCents)}</TableCell>
                        <TableCell>{r.vendas}</TableCell>
                        <TableCell>{formatarData(r.ateVendaEm)}</TableCell>
                        <TableCell className="min-w-32 whitespace-normal">{r.comprovante ?? '-'}</TableCell>
                        <TableCell><Badge variant={r.desfeitoEm ? 'neutro' : 'sucesso'}>{r.desfeitoEm ? 'Desfeito' : 'Pago'}</Badge></TableCell>
                        <TableCell>
                          <AcoesDaLinha nome={`repasse de ${r.vendedor}`} outras={[
                            !r.desfeitoEm && ultimos.has(r.id) && { rotulo: 'Desfazer', destrutiva: true, desabilitada: desfazer.isPending, aoClicar: () => desfazer.mutate(r.id) },
                          ]} />
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </Rolagem>
            )}
          </CardContent>
        </Card>
      <Dialog open={!!escolhido} onOpenChange={(aberto) => { if (!aberto) setEscolhido(null) }}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Registrar repasse</DialogTitle>
            <DialogDescription>O Pix feito ao vendedor, pelas vendas pagas até a data.</DialogDescription>
          </DialogHeader>
          {escolhido && (
            <form
              className="grid gap-4"
              onSubmit={(e) => {
                e.preventDefault()
                repassar.mutate(
                  { vendedorId: escolhido.vendedorId, ate: ate ? fimDoDiaEmBrasilia(ate) : null, comprovante: comprovante || null },
                  { onSuccess: () => { setAte(''); setComprovante(''); setEscolhido(null) } },
                )
              }}
            >
              <Erro erro={repassar.error ? mensagemDoErro(repassar.error) : null} />
              <Field>
                <FieldLabel htmlFor="vendedor-do-repasse">Vendedor</FieldLabel>
                <Select value={escolhido.vendedorId} onValueChange={(id) => setEscolhido(aPagar.find((s) => s.vendedorId === id) ?? escolhido)}>
                  <SelectTrigger id="vendedor-do-repasse" className="w-full"><SelectValue /></SelectTrigger>
                  <SelectContent>{aPagar.map((s) => <SelectItem key={s.vendedorId} value={s.vendedorId}>{s.vendedor}, {formatarCentavos(s.abertaCents)}</SelectItem>)}</SelectContent>
                </Select>
              </Field>
              {escolhido.chavePix && <p className="text-sm text-muted-foreground">Pix {escolhido.chavePix}</p>}
              <Field>
                <FieldLabel htmlFor="ate">Vendas pagas até</FieldLabel>
                <Input id="ate" type="date" value={ate} onChange={(e) => setAte(e.target.value)} />
              </Field>
              <Field>
                <FieldLabel htmlFor="comprovante">Comprovante do Pix</FieldLabel>
                <Input id="comprovante" placeholder="Id da transação" value={comprovante} onChange={(e) => setComprovante(e.target.value)} />
              </Field>
              <DialogFooter>
                <Button type="button" variant="ghost" onClick={() => setEscolhido(null)}>Cancelar</Button>
                <Button type="submit" disabled={repassar.isPending}>{/* Com data limite, o valor é o das vendas até ela: só o servidor sabe somar. */}{ate ? 'Registrar repasse' : `Registrar ${formatarCentavos(escolhido.abertaCents)}`}</Button>
              </DialogFooter>
            </form>
          )}
        </DialogContent>
      </Dialog>
    </div>
  )
}
