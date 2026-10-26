import { useState } from 'react'
import { mensagemDoErro } from '@/api/cliente'
import { formatarData } from '@/api/rotulos'
import type { SaldoAAcertar } from '@/api/tipos'
import { AcoesDaLinha } from '@/componentes/AcoesDaLinha'
import { ListaPaginada } from '@/componentes/ListaPaginada'
import { Carregando, Erro, Rolagem, Vazio } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Field, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatarCentavos, paraCentavos } from '@/dinheiro/dinheiro'
import { diaEmBrasilia } from '@/lib/data'
import { useAcertosDaRevenda, useExcluirAcerto, useRegistrarAcerto, useSaldosAAcertar } from './consultasDeEntregaEAcerto'

const emReais = (cents: number) => (cents / 100).toFixed(2).replace('.', ',')

/** O acerto de uma venda recebida por fora: valor, dia e observação. A API recusa passar do saldo. */
function RegistroDeAcerto({ saldo, aoFechar }: { saldo: SaldoAAcertar; aoFechar: () => void }) {
  const registrar = useRegistrarAcerto()
  const [valor, setValor] = useState(saldo.saldoCents > 0 ? emReais(saldo.saldoCents) : '')
  const [data, setData] = useState(() => diaEmBrasilia(new Date().toISOString()))
  const [observacao, setObservacao] = useState('')
  const cents = paraCentavos(valor)
  const valido = cents !== null && cents > 0 && data !== ''

  return (
    <Dialog open onOpenChange={(aberto) => !aberto && aoFechar()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Registrar acerto</DialogTitle>
          <DialogDescription>{saldo.revendedor}, saldo de {formatarCentavos(saldo.saldoCents)}.</DialogDescription>
        </DialogHeader>
        <form
          className="grid gap-4"
          onSubmit={(e) => {
            e.preventDefault()
            if (!valido) return
            registrar.mutate({ revendedorId: saldo.revendedorId, valorCents: cents, data, observacao: observacao.trim() || null }, { onSuccess: aoFechar })
          }}
        >
          <Erro erro={registrar.error ? mensagemDoErro(registrar.error) : null} />
          <Field>
            <FieldLabel htmlFor="valor-do-acerto">Valor</FieldLabel>
            <Input id="valor-do-acerto" inputMode="decimal" autoComplete="off" value={valor} aria-invalid={valor !== '' && cents === null} onChange={(e) => setValor(e.target.value)} />
          </Field>
          <Field>
            <FieldLabel htmlFor="data-do-acerto">Data</FieldLabel>
            <Input id="data-do-acerto" type="date" value={data} onChange={(e) => setData(e.target.value)} />
          </Field>
          <Field>
            <FieldLabel htmlFor="observacao-do-acerto">Observação (opcional)</FieldLabel>
            <Input id="observacao-do-acerto" maxLength={200} value={observacao} onChange={(e) => setObservacao(e.target.value)} />
          </Field>
          <DialogFooter>
            <Button type="button" variant="ghost" onClick={aoFechar}>Cancelar</Button>
            <Button type="submit" disabled={!valido || registrar.isPending}>Registrar acerto</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}

/** Os acertos já registrados da revenda, cada um com Excluir. */
function AcertosDaRevenda({ saldo, aoFechar }: { saldo: SaldoAAcertar; aoFechar: () => void }) {
  const acertos = useAcertosDaRevenda(saldo.revendedorId)
  const excluir = useExcluirAcerto()

  return (
    <Dialog open onOpenChange={(aberto) => !aberto && aoFechar()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Acertos de {saldo.revendedor}</DialogTitle>
          <DialogDescription>Excluir devolve o valor ao saldo.</DialogDescription>
        </DialogHeader>
        <Erro erro={excluir.error ? mensagemDoErro(excluir.error) : acertos.error ? mensagemDoErro(acertos.error) : null} />
        {acertos.isPending && <Carregando linhas={2} />}
        {acertos.data?.length === 0 && <Vazio texto="Nenhum acerto registrado." />}
        {acertos.data && acertos.data.length > 0 && (
          <ul className="grid gap-2">
            {acertos.data.map((a) => (
              <li key={a.id} className="flex items-center justify-between gap-3 border-b pb-2 text-sm last:border-b-0">
                <span className="grid">
                  <span className="font-medium tabular-nums">{formatarCentavos(a.valorCents)} em {formatarData(`${a.data}T12:00:00-03:00`)}</span>
                  {a.observacao && <span className="text-xs text-muted-foreground">{a.observacao}</span>}
                </span>
                <Button variant="ghost" className="text-destructive" disabled={excluir.isPending} onClick={() => excluir.mutate(a.id)}>Excluir</Button>
              </li>
            ))}
          </ul>
        )}
        <DialogFooter>
          <Button type="button" variant="ghost" onClick={aoFechar}>Fechar</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

/** O que cada revenda ainda deve à operadora de vendas recebidas por fora, com o registro dos acertos. */
export function PainelDeAcertos() {
  const [soComSaldo, setSoComSaldo] = useState(true)
  const lista = useSaldosAAcertar(soComSaldo)
  const [registrando, setRegistrando] = useState<SaldoAAcertar | null>(null)
  const [vendo, setVendo] = useState<SaldoAAcertar | null>(null)

  return (
    <div className="grid gap-3">
      <div className="flex justify-end">
        <Button variant="outline" aria-pressed={soComSaldo} className="max-sm:w-full" onClick={() => setSoComSaldo(!soComSaldo)}>
          {soComSaldo ? 'Ver todas as revendas' : 'Ver só com saldo'}
        </Button>
      </div>
      <Card>
        <CardContent>
          <ListaPaginada lista={lista} textoVazio={soComSaldo ? 'Nenhuma revenda com saldo a acertar.' : 'Nenhuma revenda ainda.'} nomeDaBusca="Buscar nas revendas" placeholder="Nome da revenda">
            {(itens) => (
              <Rolagem>
                <Table semBusca>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Revenda</TableHead>
                      <TableHead tipo="numero">Devido</TableHead>
                      <TableHead tipo="numero">Acertado</TableHead>
                      <TableHead tipo="numero">Saldo</TableHead>
                      <TableHead tipo="acoes" />
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {itens.map((s) => (
                      <TableRow key={s.revendedorId}>
                        <TableCell className="min-w-40 font-medium whitespace-normal">{s.revendedor}</TableCell>
                        <TableCell>{formatarCentavos(s.devidoCents)}</TableCell>
                        <TableCell>{formatarCentavos(s.acertadoCents)}</TableCell>
                        <TableCell>{s.saldoCents > 0 ? <Badge variant="atencao">{formatarCentavos(s.saldoCents)}</Badge> : formatarCentavos(s.saldoCents)}</TableCell>
                        <TableCell>
                          <AcoesDaLinha nome={s.revendedor}
                            principal={{ rotulo: 'Registrar acerto', aoClicar: () => setRegistrando(s) }}
                            outras={[{ rotulo: 'Ver acertos', aoClicar: () => setVendo(s) }]} />
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
      {registrando && <RegistroDeAcerto key={registrando.revendedorId} saldo={registrando} aoFechar={() => setRegistrando(null)} />}
      {vendo && <AcertosDaRevenda key={vendo.revendedorId} saldo={vendo} aoFechar={() => setVendo(null)} />}
    </div>
  )
}
