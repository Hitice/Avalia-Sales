import { useState } from 'react'
import { useSearchParams } from 'react-router'
import { ROTULO_DA_DESPESA, ROTULO_DA_RETIRADA } from '@/api/rotulos'
import type { DreDoMes } from '@/api/tipos'
import { Indicador, Indicadores } from '@/componentes/Indicador'
import { Cabecalho, Carregando, Erro } from '@/componentes/Tela'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { formatarCentavos } from '@/dinheiro/dinheiro'
import { LinhaDaDre } from './LinhaDaDre'
import { CartaoDoCaixa } from './CartaoDoCaixa'
import { useDre, useFecharMes, useHistorico, useMeses, useReabrirMes } from './consultas'
import { Historico, Rosca } from './Graficos'
import { PainelDeDespesas } from './PainelDeDespesas'
import { PainelDeSocios } from './PainelDeSocios'
import { Fechamento, MesFechado, SeletorDoMes } from './MesDoFinanceiro'
import { PainelDoRazao } from './PainelDoRazao'

const ABAS = ['resultado', 'despesas', 'socios', 'lancamentos'] as const

function Demonstrativo({ dre, className }: { dre: DreDoMes; className?: string }) {
  return (
    <Card className={className}>
      <CardHeader><CardTitle>Demonstrativo</CardTitle></CardHeader>
      <CardContent>
        <dl className="text-sm">
          <LinhaDaDre rotulo={`Vendido (${dre.vendas} ${dre.vendas === 1 ? 'venda' : 'vendas'})`} cents={dre.vendidoCents} />
          <LinhaDaDre rotulo="Recebido" cents={dre.recebidoCents} />
          <LinhaDaDre rotulo="A reconhecer" cents={dre.aReconhecerCents} detalhe />
          <LinhaDaDre rotulo="Receita reconhecida" cents={dre.receitaReconhecidaCents} total />
          {/* Só a venda de serviço manda parte à Avalia: sem ela no mês, a linha não aparece. */}
          {dre.parteDaAvaliaCents !== 0 && <LinhaDaDre rotulo="Parte da Avalia" cents={dre.parteDaAvaliaCents} saida />}
          <LinhaDaDre rotulo="Comissões" cents={dre.comissoesCents} saida />
          <LinhaDaDre rotulo="Custo das placas" cents={dre.custoDasPlacasCents} saida />
          <LinhaDaDre rotulo="Perdas de placa" cents={dre.perdasCents} saida />
          <LinhaDaDre rotulo="Despesas" cents={dre.despesasCents} saida />
          {dre.despesasPorCategoria.map((d) => <LinhaDaDre key={d.categoria} rotulo={ROTULO_DA_DESPESA[d.categoria]} cents={d.valorCents} saida detalhe />)}
          <LinhaDaDre rotulo="Resultado" cents={dre.resultadoCents} total />
        </dl>
        {/* Retirada não é despesa: fica fora do resultado, separada por um fio. */}
        <dl className="mt-3 border-t pt-2 text-sm">
          <LinhaDaDre rotulo="Retiradas de sócio" cents={dre.retiradasCents} saida />
          {dre.retiradasPorNatureza.map((r) => <LinhaDaDre key={r.natureza} rotulo={ROTULO_DA_RETIRADA[r.natureza]} cents={r.valorCents} saida detalhe />)}
        </dl>
      </CardContent>
    </Card>
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
  const fechar = useFecharMes()
  const reabrir = useReabrirMes()
  const d = dre.data
  const [parametros, setParametros] = useSearchParams()
  const pedida = parametros.get('aba')
  const aba = ABAS.find((a) => a === pedida) ?? 'resultado'
  const mes = competencia ?? d?.competencia
  const ondeFoi = d ? [
    { nome: 'Parte da Avalia', valorCents: d.parteDaAvaliaCents },
    { nome: 'Comissões', valorCents: d.comissoesCents },
    { nome: 'Custo das placas', valorCents: d.custoDasPlacasCents },
    { nome: 'Perdas de placa', valorCents: d.perdasCents },
    { nome: 'Despesas', valorCents: d.despesasCents },
  ] : []

  return (
    <>
      <Cabecalho titulo="Financeiro">
        {aba !== 'lancamentos' && <SeletorDoMes meses={meses.data} atual={d} valor={mes} aoTrocar={setCompetencia} />}
        {d && aba !== 'lancamentos' && <Fechamento competencia={d.competencia} fechado={d.fechado} fechar={fechar} reabrir={reabrir} />}
      </Cabecalho>
      {d?.fechado && aba !== 'lancamentos' && <MesFechado fechadoEm={d.fechadoEm} />}
      <Erro erro={dre.error} />
      <Tabs value={aba} onValueChange={(nova) => setParametros({ aba: nova }, { replace: true })}>
        <TabsList className="mb-3 w-full sm:w-auto">
          <TabsTrigger value="resultado">Resultado</TabsTrigger>
          <TabsTrigger value="despesas">Despesas</TabsTrigger>
          <TabsTrigger value="socios">Sócios</TabsTrigger>
          <TabsTrigger value="lancamentos">Lançamentos</TabsTrigger>
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
              {/* Computador: o demonstrativo (5 de 12 colunas), as saídas (4) e o caixa com as despesas (3); o histórico ocupa a linha de baixo inteira. */}
              <div className="grid items-start gap-4 lg:grid-cols-12">
                <Demonstrativo dre={d} className="lg:col-span-5" />
                <Rosca titulo="Saídas" itens={ondeFoi} vazio="Nenhuma saída no mês." empilhada className="lg:col-span-4" />
                <div className="grid content-start gap-4 lg:col-span-3">
                  <CartaoDoCaixa />
                  <Rosca titulo="Despesas" vazio="Nenhuma despesa no mês." empilhada
                    itens={d.despesasPorCategoria.map((c) => ({ nome: ROTULO_DA_DESPESA[c.categoria], valorCents: c.valorCents }))} />
                </div>
                {historico.data && <Historico meses={historico.data} className="lg:col-span-12" />}
              </div>
            </div>
          )}
        </TabsContent>
        <TabsContent value="despesas"><PainelDeDespesas competencia={mes} /></TabsContent>
        <TabsContent value="socios"><PainelDeSocios competencia={mes} /></TabsContent>
        <TabsContent value="lancamentos"><PainelDoRazao /></TabsContent>
      </Tabs>
    </>
  )
}
