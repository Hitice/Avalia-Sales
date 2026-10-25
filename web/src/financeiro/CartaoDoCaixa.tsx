import { zodResolver } from '@hookform/resolvers/zod'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { mensagemDoErro } from '@/api/cliente'
import type { Caixa } from '@/api/tipos'
import { Campo } from '@/componentes/Campo'
import { Carregando, Erro } from '@/componentes/Tela'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Field, FieldLabel } from '@/components/ui/field'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { formatarCentavos, paraCentavos } from '@/dinheiro/dinheiro'
import { reais } from '@/lib/esquemas'
import { useCaixa, useExcluirSaldoInicial, useSalvarSaldoInicial } from './consultas'
import { mesCorrente, mesSomado, nomeDoMes } from './fatias'

const esquema = z.object({ valor: reais })
type Formulario = z.infer<typeof esquema>

const centavosEmTexto = (c: number) => (c / 100).toFixed(2).replace('.', ',')

/** O mês em que o caixa começa: o corrente, o seguinte ou um dos dois anteriores, como a API aceita. */
function mesesDoInicio() {
  const agora = mesCorrente()
  return [mesSomado(agora, 1), agora, mesSomado(agora, -1), mesSomado(agora, -2)]
}

/** O saldo inicial do caixa num diálogo: incluir, trocar ou excluir. Trocar e excluir viram estorno no razão. */
function FormularioDoSaldoInicial({ caixa, aoFechar }: { caixa: Caixa; aoFechar: () => void }) {
  const salvar = useSalvarSaldoInicial()
  const excluir = useExcluirSaldoInicial()
  const [competencia, setCompetencia] = useState(caixa.inicio ?? mesSomado(mesCorrente(), 1))
  const formulario = useForm<Formulario>({
    resolver: zodResolver(esquema),
    mode: 'onChange',
    defaultValues: { valor: caixa.inicio ? centavosEmTexto(caixa.saldoInicialCents) : '' },
  })
  const opcoes = mesesDoInicio().includes(competencia) ? mesesDoInicio() : [competencia, ...mesesDoInicio()]
  const erro = salvar.error ?? excluir.error

  async function enviar(dados: Formulario) {
    try {
      await salvar.mutateAsync({ competencia, valorCents: paraCentavos(dados.valor) ?? 0 })
      aoFechar()
    } catch {
      // O erro aparece pelo estado da gravação.
    }
  }

  async function apagar() {
    try {
      await excluir.mutateAsync()
      aoFechar()
    } catch {
      // Idem.
    }
  }

  return (
    <Dialog open onOpenChange={(aberto) => !aberto && aoFechar()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Saldo inicial do caixa</DialogTitle>
          <DialogDescription>O dinheiro da revenda no primeiro dia do mês.</DialogDescription>
        </DialogHeader>
        <form className="grid gap-4" onSubmit={formulario.handleSubmit(enviar)} noValidate>
          <Erro erro={erro ? mensagemDoErro(erro) : null} />
          <Field>
            <FieldLabel htmlFor="inicio-do-caixa">Começa em</FieldLabel>
            <Select value={competencia} onValueChange={setCompetencia}>
              <SelectTrigger id="inicio-do-caixa" className="w-full"><SelectValue /></SelectTrigger>
              <SelectContent>
                {opcoes.map((m) => <SelectItem key={m} value={m}>{nomeDoMes(m)}</SelectItem>)}
              </SelectContent>
            </Select>
          </Field>
          <Campo formulario={formulario} nome="valor" rotulo="Valor (R$)" inputMode="decimal" placeholder="0,00" />
          <DialogFooter>
            {caixa.inicio && <Button type="button" variant="ghost" className="text-destructive" disabled={excluir.isPending} onClick={apagar}>Excluir</Button>}
            <Button type="submit" disabled={salvar.isPending}>Salvar</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}

/**
 * O caixa da revenda: o saldo inicial do mês em que começa, mais o que entrou
 * (vendas recebidas e aportes) e menos o que saiu (taxa, comissão repassada,
 * despesas, retiradas e pedidos de placas). Não muda com o mês escolhido.
 */
export function CartaoDoCaixa() {
  const caixa = useCaixa()
  const [aberto, setAberto] = useState(false)
  const c = caixa.data

  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between gap-3">
        <CardTitle>Caixa</CardTitle>
        {c && <Button variant="outline" size="sm" onClick={() => setAberto(true)}>{c.inicio ? 'Editar' : 'Definir saldo inicial'}</Button>}
      </CardHeader>
      <CardContent>
        <Erro erro={caixa.error} />
        {!c ? <Carregando linhas={2} /> : !c.inicio ? <p className="text-sm text-muted-foreground">Sem saldo inicial.</p> : (
          <>
            <p className={`text-2xl font-semibold tabular-nums ${c.saldoCents < 0 ? 'text-destructive' : ''}`}>{formatarCentavos(c.saldoCents)}</p>
            <dl className="mt-2 grid gap-1 text-sm">
              <div className="flex justify-between gap-3"><dt className="text-muted-foreground">Saldo inicial, {nomeDoMes(c.inicio)}</dt><dd className="tabular-nums">{formatarCentavos(c.saldoInicialCents)}</dd></div>
              <div className="flex justify-between gap-3"><dt className="text-muted-foreground">Entradas</dt><dd className="tabular-nums">{formatarCentavos(c.entradasCents)}</dd></div>
              <div className="flex justify-between gap-3"><dt className="text-muted-foreground">Saídas</dt><dd className="tabular-nums">{formatarCentavos(c.saidasCents === 0 ? 0 : -c.saidasCents)}</dd></div>
            </dl>
          </>
        )}
      </CardContent>
      {aberto && c && <FormularioDoSaldoInicial caixa={c} aoFechar={() => setAberto(false)} />}
    </Card>
  )
}
