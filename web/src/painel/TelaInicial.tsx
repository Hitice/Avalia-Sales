import { useMemo, useState } from 'react'
import { Link } from 'react-router'
import type { ResumoDasVendas, VendaDaLista } from '@/api/tipos'
import { Cabecalho, Carregando, Erro, Rolagem, Vazio } from '@/componentes/Tela'
import { Indicador, Indicadores } from '@/componentes/Indicador'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardAction, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatarCentavos } from '@/dinheiro/dinheiro'
import { useContaEncerrada } from '@/app/contaEncerrada'
import { usePlacas, useResumoDasVendas, useVendasRecentes } from './consultas'
import { porDiaDe, VendasEm30Dias } from './VendasEm30Dias'
import { nomeDoProduto, rotuloDaVenda, tomDaVenda } from './situacaoDaVenda'

const hora = new Intl.DateTimeFormat('pt-BR', { hour: '2-digit', minute: '2-digit' })
const diaDaSemana = new Intl.DateTimeFormat('pt-BR', { weekday: 'long' })
const diaEMes = new Intl.DateTimeFormat('pt-BR', { day: 'numeric', month: 'long' })

/** "Sexta, 24 de outubro". */
function dataPorExtenso(agora: number) {
  const semana = diaDaSemana.format(agora).replace('-feira', '')
  return `${semana[0].toUpperCase()}${semana.slice(1)}, ${diaEMes.format(agora)}`
}

/**
 * O resumo do dono (esboço aprovado, DESIGN.md): os quatro números do mês, as
 * vendas dos últimos 30 dias ao lado de para onde foi o dinheiro do mês, e as
 * vendas de hoje com a situação de cada uma. Avisos e pendências ficam no sino.
 */
export function TelaInicial() {
  const encerrada = useContaEncerrada()
  const placas = usePlacas({ pagina: 1, tamanho: 1 })
  const recentes = useVendasRecentes()
  const resumo = useResumoDasVendas()
  // Fixado na abertura da tela: o relógio não muda entre uma renderização e outra.
  const [agora] = useState(() => Date.now())
  const dados = useMemo(() => montar(recentes.data?.itens ?? [], resumo.data, agora), [recentes.data, resumo.data, agora])
  const porEstado = placas.data?.porEstado ?? {}
  // No celular a Nova venda já está no título da tela: o vazio não repete o botão.
  const novaVenda = !encerrada && <Button asChild variant="outline" className="max-lg:hidden"><Link to="/vender">Nova venda</Link></Button>

  const numeros = [
    { rotulo: 'Placas em estoque', valor: String(porEstado.EmEstoque ?? 0) },
    { rotulo: 'Placas no ar', valor: String((porEstado.Ativa ?? 0) + (porEstado.Carencia ?? 0)) },
    { rotulo: 'Vendas no mês', valor: String(dados.vendasNoMes) },
    { rotulo: 'Recebido no mês', valor: formatarCentavos(dados.recebidoNoMesCents) },
  ]

  return (
    <>
      <Cabecalho titulo="Início" detalhe={dataPorExtenso(agora)}>
        {/* No computador a Nova venda está no topo do painel lateral. */}
        {!encerrada && <Button asChild className="lg:hidden"><Link to="/vender">Nova venda</Link></Button>}
      </Cabecalho>
      <Erro erro={recentes.error ?? resumo.error} />
      <Indicadores>{numeros.map((n) => <Indicador key={n.rotulo} rotulo={n.rotulo} valor={n.valor} />)}</Indicadores>
      <div className="mt-3 grid gap-3 md:mt-4 md:gap-4 lg:grid-cols-12">
        <VendasEm30Dias className="lg:col-span-7" porDia={dados.porDia} carregando={resumo.isPending} />
        <Card className="lg:col-span-5">
          <CardHeader><CardTitle>Financeiro</CardTitle></CardHeader>
          <CardContent><Button asChild variant="outline"><Link to="/painel/financeiro">Ver o financeiro</Link></Button></CardContent>
        </Card>
        <Card className="lg:col-span-12">
          <CardHeader>
            <CardTitle>Vendas de hoje</CardTitle>
            <CardAction><Button asChild variant="link" size="sm" className="-my-2 -mr-2.5"><Link to="/painel/vendas">Ver todas</Link></Button></CardAction>
          </CardHeader>
          <CardContent>
            {recentes.isPending ? <Carregando linhas={3} /> : dados.hoje.length === 0 ? <Vazio texto="Nenhuma venda hoje." acao={novaVenda} /> : (
              <Rolagem>
                {/* Celular: lista de extrato, cliente em cima e valor à direita; no computador, a tabela do esboço. */}
                <ul className="-my-1 divide-y md:hidden">
                  {dados.hoje.map((v) => (
                    <li key={v.id} className="flex items-center justify-between gap-3 py-3">
                      <span className="min-w-0">
                        <span className="block truncate font-medium">{v.cliente}</span>
                        <span className="block truncate text-xs text-muted-foreground tabular-nums">{hora.format(Date.parse(v.vendidaEm))} · {item(v)}</span>
                      </span>
                      <span className="grid shrink-0 justify-items-end gap-1">
                        <span className="font-medium tabular-nums">{formatarCentavos(v.valorCents)}</span>
                        <Badge variant={tomDaVenda(v)}>{rotuloDaVenda(v)}</Badge>
                      </span>
                    </li>
                  ))}
                </ul>
                    <Table className="max-md:hidden">
                      <TableHeader>
                        <TableRow>
                          <TableHead tipo="data">Hora</TableHead>
                          <TableHead>Cliente</TableHead>
                          <TableHead>Item</TableHead>
                          <TableHead tipo="numero">Valor</TableHead>
                          <TableHead tipo="situacao">Situação</TableHead>
                        </TableRow>
                      </TableHeader>
                      <TableBody>
                        {dados.hoje.map((v) => (
                          <TableRow key={v.id}>
                            <TableCell className="text-muted-foreground">{hora.format(Date.parse(v.vendidaEm))}</TableCell>
                            <TableCell className="min-w-40 whitespace-normal">{v.cliente}</TableCell>
                            <TableCell className="min-w-40 whitespace-normal text-muted-foreground">{item(v)}</TableCell>
                            <TableCell>{formatarCentavos(v.valorCents)}</TableCell>
                            <TableCell><Badge variant={tomDaVenda(v)}>{rotuloDaVenda(v)}</Badge></TableCell>
                          </TableRow>
                        ))}
                      </TableBody>
                    </Table>
              </Rolagem>
            )}
          </CardContent>
        </Card>
      </div>
    </>
  )
}

/** "Placa K7M2PX" ou "10x Kit Mesa". */
const item = (v: VendaDaLista) => (v.codigo ? `${nomeDoProduto(v)} ${v.codigo}` : nomeDoProduto(v))

/**
 * Os números do mês e do gráfico vêm do resumo da API (as vendas inteiras, no mês de Brasília); a lista de hoje, das
 * mais recentes, com a situação de cada uma. Venda cancelada não conta nos números.
 */
function montar(recentes: VendaDaLista[], resumo: ResumoDasVendas | undefined, agora: number) {
  const hoje = new Date(agora)
  const inicioDoDia = new Date(hoje.getFullYear(), hoje.getMonth(), hoje.getDate()).getTime()

  return {
    porDia: porDiaDe((resumo?.porDia ?? []).map((d) => ({ dia: d.dia, total: d.total })), agora),
    hoje: recentes.filter((v) => Date.parse(v.vendidaEm) >= inicioDoDia).slice(0, 8),
    vendasNoMes: resumo?.noMes ?? 0,
    recebidoNoMesCents: resumo?.recebidoNoMesCents ?? 0,
  }
}
