import { Bar, BarChart, CartesianGrid, Pie, PieChart, ReferenceLine, XAxis, YAxis } from 'recharts'
import type { MesDoHistorico } from '@/api/tipos'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { type ChartConfig, ChartContainer, ChartTooltip, ChartTooltipContent } from '@/components/ui/chart'
import { formatarCentavos } from '@/dinheiro/dinheiro'
import { type Fatia, fatias, mesCurto, percentual } from './fatias'

/** Quatro cores no máximo, todas dos tokens de gráfico; "Outros", quando há, fica no cinza (--chart-4). */
const CORES = ['var(--chart-1)', 'var(--chart-2)', 'var(--chart-3)', 'var(--chart-4)']
const reaisSemCentavos = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL', maximumFractionDigits: 0, notation: 'compact' })

/** A mesma informação do gráfico, para leitor de tela. */
function TabelaOculta({ titulo, linhas }: { titulo: string; linhas: string[][] }) {
  return (
    <table className="sr-only">
      <caption>{titulo}</caption>
      <tbody>{linhas.map(([a, ...resto]) => <tr key={a}><th scope="row">{a}</th>{resto.map((r, i) => <td key={i}>{r}</td>)}</tr>)}</tbody>
    </table>
  )
}

/** Pizza em rosca de até quatro partes, com a legenda ao lado: nome, valor e percentual de cada parte. */
export function Rosca({ titulo, itens, vazio }: { titulo: string; itens: Fatia[]; vazio: string }) {
  const partes = fatias(itens)
  const total = partes.reduce((s, p) => s + p.valorCents, 0)
  const dados = partes.map((p, i) => ({ chave: `f${i}`, nome: p.nome, valor: p.valorCents, fill: `var(--color-f${i})` }))
  const config = Object.fromEntries(dados.map((d, i) => [d.chave, { label: d.nome, color: CORES[i] }])) satisfies ChartConfig

  return (
    <Card>
      <CardHeader><CardTitle>{titulo}</CardTitle></CardHeader>
      <CardContent>
        {partes.length === 0 ? <p className="text-sm text-muted-foreground">{vazio}</p> : (
          <div className="grid items-center gap-4 sm:grid-cols-[10rem_minmax(0,1fr)]">
            <ChartContainer config={config} className="mx-auto aspect-square h-40">
              <PieChart>
                <ChartTooltip content={<ChartTooltipContent hideLabel nameKey="chave" formatter={(valor, _nome, item) => (
                  <span className="flex w-full justify-between gap-3"><span>{(item.payload as { nome: string }).nome}</span><span className="tabular-nums">{formatarCentavos(Number(valor))}</span></span>
                )} />} />
                <Pie data={dados} dataKey="valor" nameKey="nome" innerRadius="58%" outerRadius="100%" strokeWidth={2} stroke="var(--card)" isAnimationActive={false} />
              </PieChart>
            </ChartContainer>
            <ul className="grid gap-2 text-sm">
              {dados.map((d, i) => (
                <li key={d.chave} className="flex items-center gap-2">
                  <span aria-hidden className="size-2.5 shrink-0 rounded-full" style={{ background: CORES[i] }} />
                  <span className="min-w-0 flex-1 truncate">{d.nome}</span>
                  <span className="tabular-nums">{formatarCentavos(d.valor)}</span>
                  <span className="w-10 text-right text-muted-foreground tabular-nums">{percentual(d.valor, total)}</span>
                </li>
              ))}
            </ul>
          </div>
        )}
      </CardContent>
    </Card>
  )
}

const configDoHistorico = {
  recebido: { label: 'Recebido', color: 'var(--chart-1)' },
  resultado: { label: 'Resultado', color: 'var(--chart-2)' },
} satisfies ChartConfig

/** Recebido e resultado dos últimos seis meses, lado a lado. Resultado negativo desce abaixo da linha do zero. */
export function Historico({ meses }: { meses: MesDoHistorico[] }) {
  const dados = meses.map((m) => ({ mes: mesCurto(m.competencia), recebido: m.recebidoCents / 100, resultado: m.resultadoCents / 100 }))
  return (
    <Card>
      <CardHeader><CardTitle>Últimos 6 meses</CardTitle></CardHeader>
      <CardContent>
        <ChartContainer config={configDoHistorico} className="aspect-auto h-56 w-full">
          <BarChart accessibilityLayer data={dados} margin={{ left: 4, right: 4 }}>
            <CartesianGrid vertical={false} />
            <XAxis dataKey="mes" tickLine={false} axisLine={false} tickMargin={8} />
            <YAxis tickLine={false} axisLine={false} width={56} tickFormatter={(v: number) => reaisSemCentavos.format(v)} />
            <ReferenceLine y={0} stroke="var(--border)" />
            <ChartTooltip cursor={false} content={<ChartTooltipContent formatter={(valor, nome) => (
              <span className="flex w-full justify-between gap-3">
                <span>{configDoHistorico[nome as keyof typeof configDoHistorico]?.label ?? nome}</span>
                <span className="tabular-nums">{formatarCentavos(Math.round(Number(valor) * 100))}</span>
              </span>
            )} />} />
            <Bar dataKey="recebido" fill="var(--color-recebido)" radius={[4, 4, 0, 0]} maxBarSize={18} />
            <Bar dataKey="resultado" fill="var(--color-resultado)" radius={[4, 4, 0, 0]} maxBarSize={18} />
          </BarChart>
        </ChartContainer>
        <div className="mt-2 flex justify-center gap-4 text-xs text-muted-foreground">
          {Object.entries(configDoHistorico).map(([chave, c]) => (
            <span key={chave} className="flex items-center gap-1.5"><span aria-hidden className="size-2.5 rounded-sm" style={{ background: c.color }} />{c.label}</span>
          ))}
        </div>
        <TabelaOculta titulo="Recebido e resultado por mês" linhas={meses.map((m) => [mesCurto(m.competencia), formatarCentavos(m.recebidoCents), formatarCentavos(m.resultadoCents)])} />
      </CardContent>
    </Card>
  )
}
