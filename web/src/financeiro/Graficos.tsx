import { Bar, BarChart, CartesianGrid, Pie, PieChart, ReferenceLine, XAxis, YAxis } from 'recharts'
import type { MesDoHistorico } from '@/api/tipos'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { type ChartConfig, ChartContainer, ChartTooltip, ChartTooltipContent } from '@/components/ui/chart'
import { formatarCentavos } from '@/dinheiro/dinheiro'
import { type Fatia, fatias, mesCurto, percentual } from './fatias'

/** Quatro cores no máximo, todas dos tokens de gráfico; "Outros", quando há, fica no cinza (--chart-4). */
const CORES = ['var(--chart-1)', 'var(--chart-2)', 'var(--chart-3)', 'var(--chart-4)']
/* O quadradinho da legenda, na mesma ordem: classe de token, nunca cor em style. */
const QUADRADOS = ['bg-chart-1', 'bg-chart-2', 'bg-chart-3', 'bg-chart-4']
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

/**
 * Pizza em rosca de até quatro partes, com a legenda ao lado: nome, valor e
 * percentual de cada parte. Com `primeira`, essa parte fica sempre na cor da
 * marca (chart-1) e o percentual dela vai no centro; o resto divide as outras
 * três cores, da maior para a menor.
 */
export function Rosca({ titulo, itens, vazio, primeira, className, empilhada = false }: { titulo: string; itens: Fatia[]; vazio: string; primeira?: Fatia; className?: string; empilhada?: boolean }) {
  const fixa = primeira && primeira.valorCents > 0 ? primeira : null
  // Com `primeira`, a cor da marca é dela: sem ela no mês (zero ou negativa), a cor fica sem uso, e não vai para outra parte.
  const deslocamento = primeira && !fixa ? 1 : 0
  const partes = fixa ? [fixa, ...fatias(itens, 3)] : fatias(itens, 4 - deslocamento)
  const total = partes.reduce((s, p) => s + p.valorCents, 0)
  const dados = partes.map((p, i) => ({ chave: `f${i}`, nome: p.nome, valor: p.valorCents, fill: `var(--color-f${i})` }))
  const config = Object.fromEntries(dados.map((d, i) => [d.chave, { label: d.nome, color: CORES[i + deslocamento] }])) satisfies ChartConfig

  return (
    <Card className={className}>
      <CardHeader><CardTitle>{titulo}</CardTitle></CardHeader>
      <CardContent>
        {partes.length === 0 ? <p className="text-sm text-muted-foreground">{vazio}</p> : (
          <div className={empilhada ? 'grid items-center gap-4' : 'grid items-center gap-5 sm:lateral-e-lista-32'}>
            <div className="relative mx-auto size-32">
              <ChartContainer config={config} className="aspect-square size-32">
                <PieChart>
                  <ChartTooltip content={<ChartTooltipContent hideLabel nameKey="chave" formatter={(valor, _nome, item) => (
                    <span className="flex w-full justify-between gap-3"><span>{(item.payload as { nome: string }).nome}</span><span className="tabular-nums">{formatarCentavos(Number(valor))}</span></span>
                  )} />} />
                  <Pie data={dados} dataKey="valor" nameKey="nome" innerRadius="76%" outerRadius="100%" strokeWidth={2} stroke="var(--card)" isAnimationActive={false} />
                </PieChart>
              </ChartContainer>
              {fixa && (
                <p aria-hidden className="pointer-events-none absolute inset-0 grid place-content-center text-center leading-tight">
                  <span className="text-2xs text-muted-foreground">{fixa.nome}</span>
                  <span className="text-sm font-medium tabular-nums">{percentual(fixa.valorCents, total)}</span>
                </p>
              )}
            </div>
            <ul className="grid gap-2 text-sm">
              {dados.map((d, i) => (
                <li key={d.chave} className="flex items-center gap-2">
                  <span aria-hidden className={`size-2 shrink-0 rounded-xs ${QUADRADOS[i + deslocamento]}`} />
                  <span className="min-w-0 flex-1 truncate">{d.nome}</span>
                  <span className="font-medium tabular-nums">{formatarCentavos(d.valor)}</span>
                  <span className="w-9 text-right text-muted-foreground tabular-nums">{percentual(d.valor, total)}</span>
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
        <ChartContainer config={configDoHistorico} className="aspect-auto h-40 w-full">
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
            <Bar dataKey="recebido" fill="var(--color-recebido)" radius={[2, 2, 0, 0]} maxBarSize={16} />
            <Bar dataKey="resultado" fill="var(--color-resultado)" radius={[2, 2, 0, 0]} maxBarSize={16} />
          </BarChart>
        </ChartContainer>
        <div className="mt-2 flex justify-center gap-4 text-xs text-muted-foreground">
          {Object.entries(configDoHistorico).map(([chave, c]) => (
            <span key={chave} className="flex items-center gap-1.5"><span aria-hidden className={`size-2.5 rounded-sm ${chave === 'recebido' ? 'bg-chart-1' : 'bg-chart-2'}`} />{c.label}</span>
          ))}
        </div>
        <TabelaOculta titulo="Recebido e resultado por mês" linhas={meses.map((m) => [mesCurto(m.competencia), formatarCentavos(m.recebidoCents), formatarCentavos(m.resultadoCents)])} />
      </CardContent>
    </Card>
  )
}
