import type { ReactNode } from 'react'
import { Bar, BarChart, CartesianGrid, Cell, XAxis, YAxis } from 'recharts'
import type { VendaDaLista } from '@/api/tipos'
import { Carregando } from '@/componentes/Tela'
import { Card, CardAction, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { type ChartConfig, ChartContainer, ChartTooltip, ChartTooltipContent } from '@/components/ui/chart'

const DIA = 86_400_000
const diaCurto = new Intl.DateTimeFormat('pt-BR', { day: '2-digit', month: '2-digit' })

/** Vendas por dia nos últimos 30 dias, até hoje. Venda cancelada não conta. */
export function vendasPorDia(vendas: VendaDaLista[], agora: number) {
  const valendo = vendas.filter((v) => v.situacao === 'Registrada')
  const hoje = new Date(agora)
  const inicioDoDia = new Date(hoje.getFullYear(), hoje.getMonth(), hoje.getDate()).getTime()
  return Array.from({ length: 30 }, (_, i) => {
    const inicio = inicioDoDia - (29 - i) * DIA
    return { dia: diaCurto.format(inicio), total: valendo.filter((v) => { const t = Date.parse(v.vendidaEm); return t >= inicio && t < inicio + DIA }).length }
  })
}

/** Uma contagem por dia (dia em AAAA-MM-DD) nos últimos 30 dias até hoje; dia sem registro entra com zero. */
export function porDiaDe(contagens: { dia: string; total: number }[], agora: number) {
  const hoje = new Date(agora)
  return Array.from({ length: 30 }, (_, i) => {
    const d = new Date(hoje.getFullYear(), hoje.getMonth(), hoje.getDate() - (29 - i))
    const chave = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
    return { dia: diaCurto.format(d), total: contagens.find((c) => c.dia === chave)?.total ?? 0 }
  })
}

/**
 * O cartão do gráfico de 30 dias: vendas (a do revendedor mostra a revenda
 * inteira; a do vendedor, só as dele, que a API já filtra) ou leituras da placa.
 */
export function VendasEm30Dias({ porDia, carregando, className, titulo = 'Vendas nos últimos 30 dias', serie = 'Vendas', detalhe }: {
  porDia: ReturnType<typeof vendasPorDia>; carregando: boolean; className?: string; titulo?: string; serie?: string; detalhe?: ReactNode
}) {
  const config = { total: { label: serie, color: 'var(--chart-1)' } } satisfies ChartConfig
  return (
    <Card className={className}>
      <CardHeader>
        <CardTitle>{titulo}</CardTitle>
        <CardAction className="text-xs text-muted-foreground tabular-nums">{porDia[0].dia} a {porDia[29].dia}</CardAction>
      </CardHeader>
      <CardContent>
        {detalhe}
        {carregando ? <Carregando linhas={3} /> : (
          <>
            <ChartContainer config={config} className="aspect-auto h-48 w-full">
              <BarChart accessibilityLayer data={porDia} margin={{ left: -20, right: 16, top: 4 }} barCategoryGap={2}>
                <CartesianGrid vertical={false} />
                <XAxis dataKey="dia" tickLine={false} axisLine={false} tickMargin={8} interval={0} ticks={[0, 7, 14, 21, 28].map((i) => porDia[i].dia)} />
                <YAxis allowDecimals={false} tickLine={false} axisLine={false} width={44} />
                <ChartTooltip cursor={false} content={<ChartTooltipContent />} />
                {/* Dia sem venda fica num traço cinza de 2 px: a falta aparece sem parecer dado. */}
                <Bar dataKey="total" radius={[2, 2, 0, 0]} maxBarSize={14} minPointSize={2} isAnimationActive={false}>
                  {porDia.map((p) => <Cell key={p.dia} fill={p.total > 0 ? 'var(--color-total)' : 'var(--chart-4)'} />)}
                </Bar>
              </BarChart>
            </ChartContainer>
            <table className="sr-only">
              <caption>{serie} por dia</caption>
              <tbody>{porDia.map((p) => <tr key={p.dia}><th scope="row">{p.dia}</th><td>{p.total}</td></tr>)}</tbody>
            </table>
          </>
        )}
      </CardContent>
    </Card>
  )
}
