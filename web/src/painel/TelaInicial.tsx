import { useMemo, useState } from 'react'
import { Link } from 'react-router'
import { Bar, BarChart, CartesianGrid, XAxis, YAxis } from 'recharts'
import type { VendaDaLista } from '@/api/tipos'
import { Cabecalho, Carregando, Erro } from '@/componentes/Tela'
import { Indicador, Indicadores } from '@/componentes/Indicador'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { type ChartConfig, ChartContainer, ChartTooltip, ChartTooltipContent } from '@/components/ui/chart'
import { formatarCentavos } from '@/dinheiro/dinheiro'
import { usePlacas, useVendas } from './consultas'

const DIA = 86_400_000
const diaCurto = new Intl.DateTimeFormat('pt-BR', { day: '2-digit', month: '2-digit' })
const mesCurto = new Intl.DateTimeFormat('pt-BR', { month: 'short' })
const reaisSemCentavos = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL', maximumFractionDigits: 0 })

const configDasVendas = { vendas: { label: 'Vendas', color: 'var(--chart-1)' } } satisfies ChartConfig
const configDoRecebido = { reais: { label: 'Recebido', color: 'var(--chart-1)' } } satisfies ChartConfig

/** O resumo do dono: os números do mês e três gráficos das vendas. Avisos e pendências ficam no sino. */
export function TelaInicial() {
  const placas = usePlacas({ pagina: 1 })
  const vendas = useVendas()
  // Fixado na abertura da tela: o relógio não muda entre uma renderização e outra.
  const [agora] = useState(() => Date.now())
  const dados = useMemo(() => montar(vendas.data ?? [], agora), [vendas.data, agora])
  const porEstado = placas.data?.porEstado ?? {}

  const numeros = [
    { rotulo: 'Placas em estoque', valor: String(porEstado.EmEstoque ?? 0) },
    { rotulo: 'Placas no ar', valor: String((porEstado.Ativa ?? 0) + (porEstado.Carencia ?? 0)) },
    { rotulo: 'Vendas no mês', valor: String(dados.vendasNoMes) },
    { rotulo: 'Recebido no mês', valor: formatarCentavos(dados.recebidoNoMesCents) },
  ]

  return (
    <>
      <Cabecalho titulo="Início">
        <Button asChild><Link to="/vender">Nova venda</Link></Button>
      </Cabecalho>
      <Erro erro={vendas.error} />
      <Indicadores>{numeros.map((n) => <Indicador key={n.rotulo} rotulo={n.rotulo} valor={n.valor} />)}</Indicadores>
      {vendas.isPending ? <div className="mt-4"><Carregando linhas={2} /></div> : (
        <div className="mt-4 grid gap-4 xl:grid-cols-2">
          <Card className="xl:col-span-2">
            <CardHeader><CardTitle>Vendas nos últimos 30 dias</CardTitle></CardHeader>
            <CardContent>
              <ChartContainer config={configDasVendas} className="aspect-auto h-56 w-full">
                <BarChart accessibilityLayer data={dados.porDia} margin={{ left: -16, right: 4 }}>
                  <CartesianGrid vertical={false} />
                  <XAxis dataKey="dia" tickLine={false} axisLine={false} tickMargin={8} minTickGap={24} />
                  <YAxis allowDecimals={false} tickLine={false} axisLine={false} width={40} />
                  <ChartTooltip cursor={false} content={<ChartTooltipContent />} />
                  <Bar dataKey="vendas" fill="var(--color-vendas)" radius={[4, 4, 0, 0]} maxBarSize={18} />
                </BarChart>
              </ChartContainer>
              <TabelaOculta titulo="Vendas por dia" linhas={dados.porDia.map((d) => [d.dia, String(d.vendas)])} />
            </CardContent>
          </Card>
          <Card>
            <CardHeader><CardTitle>Recebido por mês</CardTitle></CardHeader>
            <CardContent>
              <ChartContainer config={configDoRecebido} className="aspect-auto h-56 w-full">
                <BarChart accessibilityLayer data={dados.porMes} margin={{ left: 4, right: 4 }}>
                  <CartesianGrid vertical={false} />
                  <XAxis dataKey="mes" tickLine={false} axisLine={false} tickMargin={8} />
                  <YAxis tickLine={false} axisLine={false} width={72} tickFormatter={(v: number) => reaisSemCentavos.format(v)} />
                  <ChartTooltip cursor={false} content={<ChartTooltipContent formatter={(v) => formatarCentavos(Math.round(Number(v) * 100))} />} />
                  <Bar dataKey="reais" fill="var(--color-reais)" radius={[4, 4, 0, 0]} maxBarSize={36} />
                </BarChart>
              </ChartContainer>
              <TabelaOculta titulo="Recebido por mês" linhas={dados.porMes.map((m) => [m.mes, formatarCentavos(Math.round(m.reais * 100))])} />
            </CardContent>
          </Card>
          <Card>
            <CardHeader><CardTitle>Vendas do mês por vendedor</CardTitle></CardHeader>
            <CardContent>
              {dados.porVendedor.length === 0
                ? <p className="text-sm text-muted-foreground">Nenhuma venda no mês.</p>
                : (
                    <ChartContainer config={configDasVendas} className="aspect-auto w-full" style={{ height: Math.max(120, dados.porVendedor.length * 40) }}>
                      <BarChart accessibilityLayer data={dados.porVendedor} layout="vertical" margin={{ left: 4, right: 16 }}>
                        <CartesianGrid horizontal={false} />
                        <XAxis type="number" allowDecimals={false} tickLine={false} axisLine={false} />
                        <YAxis type="category" dataKey="vendedor" tickLine={false} axisLine={false} width={120}
                          tickFormatter={(nome: string) => (nome.length > 16 ? `${nome.slice(0, 15)}…` : nome)} />
                        <ChartTooltip cursor={false} content={<ChartTooltipContent />} />
                        <Bar dataKey="vendas" fill="var(--color-vendas)" radius={[0, 4, 4, 0]} maxBarSize={20} />
                      </BarChart>
                    </ChartContainer>
                  )}
              <TabelaOculta titulo="Vendas por vendedor" linhas={dados.porVendedor.map((v) => [v.vendedor, String(v.vendas)])} />
            </CardContent>
          </Card>
        </div>
      )}
    </>
  )
}

/** A mesma informação do gráfico, para leitor de tela. */
function TabelaOculta({ titulo, linhas }: { titulo: string; linhas: string[][] }) {
  return (
    <table className="sr-only">
      <caption>{titulo}</caption>
      <tbody>{linhas.map(([a, b]) => <tr key={a}><th scope="row">{a}</th><td>{b}</td></tr>)}</tbody>
    </table>
  )
}

/** Venda cancelada não conta; recebido é o que foi pago, no mês do pagamento. */
function montar(vendas: VendaDaLista[], agora: number) {
  const valendo = vendas.filter((v) => v.situacao === 'Registrada')
  const hoje = new Date(agora)
  const inicioDoMes = new Date(hoje.getFullYear(), hoje.getMonth(), 1).getTime()
  const inicioDoDia = new Date(hoje.getFullYear(), hoje.getMonth(), hoje.getDate()).getTime()

  const porDia = Array.from({ length: 30 }, (_, i) => {
    const inicio = inicioDoDia - (29 - i) * DIA
    return { dia: diaCurto.format(inicio), vendas: valendo.filter((v) => { const t = Date.parse(v.vendidaEm); return t >= inicio && t < inicio + DIA }).length }
  })

  const porMes = Array.from({ length: 6 }, (_, i) => {
    const inicio = new Date(hoje.getFullYear(), hoje.getMonth() - (5 - i), 1)
    const fim = new Date(inicio.getFullYear(), inicio.getMonth() + 1, 1).getTime()
    const cents = valendo.filter((v) => v.pagamento === 'Paga' && v.pagaEm && Date.parse(v.pagaEm) >= inicio.getTime() && Date.parse(v.pagaEm) < fim)
      .reduce((s, v) => s + v.valorCents, 0)
    return { mes: mesCurto.format(inicio).replace('.', ''), reais: cents / 100 }
  })

  const doMes = valendo.filter((v) => Date.parse(v.vendidaEm) >= inicioDoMes)
  const contagem = new Map<string, number>()
  for (const v of doMes) contagem.set(v.vendedor, (contagem.get(v.vendedor) ?? 0) + 1)
  const porVendedor = [...contagem].map(([vendedor, n]) => ({ vendedor, vendas: n })).sort((a, b) => b.vendas - a.vendas).slice(0, 8)

  return {
    porDia,
    porMes,
    porVendedor,
    vendasNoMes: doMes.length,
    recebidoNoMesCents: doMes.filter((v) => v.pagamento === 'Paga').reduce((s, v) => s + v.valorCents, 0),
  }
}
