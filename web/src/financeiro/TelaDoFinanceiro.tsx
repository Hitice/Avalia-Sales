import { useState } from 'react'
import { mensagemDoErro } from '@/api/cliente'
import { ROTULO_DA_DESPESA, ROTULO_DA_RETIRADA, formatarData } from '@/api/rotulos'
import type { DreDoMes } from '@/api/tipos'
import { Indicador, Indicadores } from '@/componentes/Indicador'
import { Cabecalho, Carregando, Erro } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Field, FieldLabel } from '@/components/ui/field'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { Textarea } from '@/components/ui/textarea'
import { formatarCentavos } from '@/dinheiro/dinheiro'
import { cn } from '@/lib/utils'
import { useDre, useFecharMes, useHistorico, useMeses, useReabrirMes } from './consultas'
import { mesCorrente, nomeDoMes } from './fatias'
import { Historico, Rosca } from './Graficos'
import { PainelDeDespesas } from './PainelDeDespesas'
import { PainelDeSocios } from './PainelDeSocios'

/** Uma linha da DRE: rótulo e valor; a saída vem com o sinal de menos, e o recuo marca o detalhe. */
function Linha({ rotulo, cents, saida = false, detalhe = false, total = false }: { rotulo: string; cents: number; saida?: boolean; detalhe?: boolean; total?: boolean }) {
  return (
    <div className={cn('flex items-baseline justify-between gap-3 py-1.5', detalhe && 'pl-4 text-sm text-muted-foreground', total && 'mt-1 border-t pt-3 font-semibold')}>
      <dt>{rotulo}</dt>
      <dd className={cn('tabular-nums', total && cents < 0 && 'text-destructive')}>{formatarCentavos(saida && cents !== 0 ? -cents : cents)}</dd>
    </div>
  )
}

function Demonstrativo({ dre }: { dre: DreDoMes }) {
  return (
    <Card>
      <CardHeader><CardTitle>Resultado do mês</CardTitle></CardHeader>
      <CardContent>
        <dl>
          <Linha rotulo={`Vendido (${dre.vendas} ${dre.vendas === 1 ? 'venda' : 'vendas'})`} cents={dre.vendidoCents} />
          <Linha rotulo="Recebido" cents={dre.recebidoCents} />
          <Linha rotulo="A reconhecer" cents={dre.aReconhecerCents} detalhe />
          <Linha rotulo="Receita reconhecida" cents={dre.receitaReconhecidaCents} total />
          <Linha rotulo="Taxa da plataforma" cents={dre.taxaCents} saida />
          <Linha rotulo="Comissões" cents={dre.comissoesCents} saida />
          <Linha rotulo="Custo das placas" cents={dre.custoDasPlacasCents} saida />
          <Linha rotulo="Perdas de placa" cents={dre.perdasCents} saida />
          <Linha rotulo="Despesas" cents={dre.despesasCents} saida />
          {dre.despesasPorCategoria.map((d) => <Linha key={d.categoria} rotulo={ROTULO_DA_DESPESA[d.categoria]} cents={d.valorCents} saida detalhe />)}
          <Linha rotulo="Resultado" cents={dre.resultadoCents} total />
        </dl>
        <dl className="mt-4 rounded-lg bg-muted px-3 py-1">
          <Linha rotulo="Retiradas de sócio" cents={dre.retiradasCents} saida />
          {dre.retiradasPorNatureza.map((r) => <Linha key={r.natureza} rotulo={ROTULO_DA_RETIRADA[r.natureza]} cents={r.valorCents} saida detalhe />)}
        </dl>
      </CardContent>
    </Card>
  )
}

/** Fechar o mês, com confirmação; reabrir pede o motivo, que vai à auditoria. */
function Fechamento({ competencia, fechado }: { competencia: string; fechado: boolean }) {
  const fechar = useFecharMes()
  const reabrir = useReabrirMes()
  const [aberto, setAberto] = useState(false)
  const [motivo, setMotivo] = useState('')
  const gravacao = fechado ? reabrir : fechar
  // Só mês que já acabou se fecha.
  if (!fechado && competencia >= mesCorrente()) return null

  async function confirmar() {
    try {
      if (fechado) await reabrir.mutateAsync({ competencia, motivo })
      else await fechar.mutateAsync(competencia)
      setAberto(false)
      setMotivo('')
    } catch {
      // O erro aparece no diálogo.
    }
  }

  return (
    <>
      <Button variant="outline" onClick={() => { gravacao.reset(); setAberto(true) }}>{fechado ? 'Reabrir mês' : 'Fechar mês'}</Button>
      <Dialog open={aberto} onOpenChange={setAberto}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{fechado ? 'Reabrir' : 'Fechar'} {nomeDoMes(competencia)}</DialogTitle>
            <DialogDescription>{fechado ? 'Lançamentos voltam a entrar no mês.' : 'Correções depois vão para o mês corrente.'}</DialogDescription>
          </DialogHeader>
          <Erro erro={gravacao.error ? mensagemDoErro(gravacao.error) : null} />
          {fechado && (
            <Field>
              <FieldLabel htmlFor="motivo">Motivo</FieldLabel>
              <Textarea id="motivo" maxLength={200} value={motivo} onChange={(e) => setMotivo(e.target.value)} />
            </Field>
          )}
          <DialogFooter>
            <Button variant="ghost" onClick={() => setAberto(false)}>Cancelar</Button>
            <Button disabled={gravacao.isPending || (fechado && !motivo.trim())} onClick={confirmar}>{fechado ? 'Reabrir mês' : 'Fechar mês'}</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  )
}

/**
 * O financeiro do dono: a DRE do mês, os gráficos de para onde foi o dinheiro,
 * despesas, sócios e retiradas, e o fechamento do mês. Tudo sai do razão.
 */
export function TelaDoFinanceiro() {
  const meses = useMeses()
  const [competencia, setCompetencia] = useState<string | undefined>()
  const dre = useDre(competencia)
  const historico = useHistorico(competencia)
  const d = dre.data
  const mes = competencia ?? d?.competencia
  const ondeFoi = d ? [
    { nome: 'Taxa da plataforma', valorCents: d.taxaCents },
    { nome: 'Comissões', valorCents: d.comissoesCents },
    { nome: 'Custo das placas', valorCents: d.custoDasPlacasCents },
    { nome: 'Perdas de placa', valorCents: d.perdasCents },
    { nome: 'Despesas', valorCents: d.despesasCents },
  ] : []

  return (
    <>
      <Cabecalho titulo="Financeiro">
        <Select value={mes ?? ''} onValueChange={setCompetencia}>
          <SelectTrigger aria-label="Mês" className="w-48"><SelectValue placeholder="Mês" /></SelectTrigger>
          <SelectContent>
            {(meses.data ?? (d ? [{ competencia: d.competencia, fechado: d.fechado }] : [])).map((m) => (
              <SelectItem key={m.competencia} value={m.competencia}>{nomeDoMes(m.competencia)}{m.fechado ? ' (fechado)' : ''}</SelectItem>
            ))}
          </SelectContent>
        </Select>
        {d && <Fechamento competencia={d.competencia} fechado={d.fechado} />}
      </Cabecalho>
      {d?.fechado && <p className="mb-3 text-sm text-muted-foreground"><Badge variant="neutro">Fechado</Badge> em {formatarData(d.fechadoEm)}</p>}
      <Erro erro={dre.error} />
      <Tabs defaultValue="resultado">
        <TabsList className="mb-3 w-full sm:w-auto">
          <TabsTrigger value="resultado">Resultado</TabsTrigger>
          <TabsTrigger value="despesas">Despesas</TabsTrigger>
          <TabsTrigger value="socios">Sócios</TabsTrigger>
        </TabsList>
        <TabsContent value="resultado">
          {!d ? <Carregando linhas={3} /> : (
            <div className="grid gap-4">
              <Indicadores>
                <Indicador rotulo="Recebido" valor={formatarCentavos(d.recebidoCents)} />
                <Indicador rotulo="Resultado" valor={formatarCentavos(d.resultadoCents)} />
                <Indicador rotulo="Despesas" valor={formatarCentavos(d.despesasCents)} />
                <Indicador rotulo="Retiradas" valor={formatarCentavos(d.retiradasCents)} />
              </Indicadores>
              <div className="grid gap-4 xl:grid-cols-2">
                <Demonstrativo dre={d} />
                <div className="grid content-start gap-4">
                  <Rosca titulo="Para onde foi o dinheiro" itens={ondeFoi} vazio="Nenhuma saída no mês." />
                  <Rosca titulo="Despesas por categoria" vazio="Nenhuma despesa no mês."
                    itens={d.despesasPorCategoria.map((c) => ({ nome: ROTULO_DA_DESPESA[c.categoria], valorCents: c.valorCents }))} />
                </div>
              </div>
              {historico.data && <Historico meses={historico.data} />}
            </div>
          )}
        </TabsContent>
        <TabsContent value="despesas"><PainelDeDespesas competencia={mes} /></TabsContent>
        <TabsContent value="socios"><PainelDeSocios competencia={mes} /></TabsContent>
      </Tabs>
    </>
  )
}
