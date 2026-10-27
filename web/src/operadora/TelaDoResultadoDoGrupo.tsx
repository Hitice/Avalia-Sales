import { useState } from 'react'
import type { LinhaDoResultado, ResultadoDoGrupo } from '@/api/tipos'
import { Indicador, Indicadores } from '@/componentes/Indicador'
import { ListaPaginada } from '@/componentes/ListaPaginada'
import { Aviso, Cabecalho, Carregando, Erro, Rolagem } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { formatarCentavos } from '@/dinheiro/dinheiro'
import { Fechamento, MesFechado, SeletorDoMes } from '@/financeiro/MesDoFinanceiro'
import { cn } from '@/lib/utils'
import { PainelDeAcertos } from './PainelDeAcertos'
import { PainelDeCustosDeLote } from './PainelDeCustosDeLote'
import { PainelDeDespesasDaOperadora } from './PainelDeDespesasDaOperadora'
import { useFecharMesDaOperadora, useMesesDaOperadora, useReabrirMesDaOperadora, useReceitaPorRevenda, useResultadoDoGrupo } from './consultasFinanceiras'

/** Zero vira traço: a coluna de eliminações fica legível quando a maior parte das linhas não tem nada a eliminar. */
const valor = (cents: number) => (cents === 0 ? '-' : formatarCentavos(cents))

function LinhaDaTabela({ linha }: { linha: LinhaDoResultado }) {
  const destaque = linha.tipo === 'Total' || linha.tipo === 'Resultado'
  return (
    <TableRow className={cn(destaque && 'font-medium')}>
      <TableCell className="min-w-48 whitespace-normal">{linha.rotulo}</TableCell>
      <TableCell>{valor(linha.revendaCents)}</TableCell>
      <TableCell>{valor(linha.operadoraCents)}</TableCell>
      <TableCell>{valor(linha.eliminacoesCents)}</TableCell>
      <TableCell className={cn(linha.tipo === 'Resultado' && linha.consolidadoCents < 0 && 'text-destructive')}>{valor(linha.consolidadoCents)}</TableCell>
    </TableRow>
  )
}

/** O demonstrativo do mês: a revenda do grupo, a operadora, o que se elimina entre as duas e o consolidado. */
function Demonstrativo({ resultado }: { resultado: ResultadoDoGrupo }) {
  return (
    <Card>
      <CardHeader><CardTitle>Demonstrativo</CardTitle></CardHeader>
      <CardContent>
        <Rolagem>
          <Table semBusca>
            <TableHeader>
              <TableRow>
                <TableHead>Linha</TableHead>
                <TableHead tipo="numero">Revenda do grupo</TableHead>
                <TableHead tipo="numero">Operadora</TableHead>
                <TableHead tipo="numero">Eliminações</TableHead>
                <TableHead tipo="numero">Consolidado</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>{resultado.linhas.map((l) => <LinhaDaTabela key={l.chave} linha={l} />)}</TableBody>
          </Table>
        </Rolagem>
        {resultado.retiradasDeSocioCents !== 0 && (
          <p className="mt-3 text-sm text-muted-foreground">Retiradas de sócio da revenda do grupo, fora do resultado: {formatarCentavos(resultado.retiradasDeSocioCents)}.</p>
        )}
      </CardContent>
    </Card>
  )
}

/** Cada transação interna lida de dois lados: a diferença é zero quando o livro da operadora e o razão da revenda estão completos. */
function Conciliacao({ resultado }: { resultado: ResultadoDoGrupo }) {
  const fecha = resultado.diferencaDaConciliacaoCents === 0
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex flex-wrap items-center gap-2">
          Conciliação das eliminações
          <Badge variant={fecha ? 'sucesso' : 'erro'}>{fecha ? 'Fecha em zero' : `Diferença de ${formatarCentavos(resultado.diferencaDaConciliacaoCents)}`}</Badge>
        </CardTitle>
      </CardHeader>
      <CardContent>
        <Rolagem>
          <Table semBusca>
            <TableHeader>
              <TableRow>
                <TableHead>Transação interna</TableHead>
                <TableHead tipo="numero">Na revenda do grupo</TableHead>
                <TableHead tipo="numero">Na operadora</TableHead>
                <TableHead tipo="numero">Diferença</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {resultado.conciliacao.map((c) => (
                <TableRow key={c.chave}>
                  <TableCell className="min-w-48 whitespace-normal">{c.rotulo}</TableCell>
                  <TableCell>{valor(c.naRevendaCents)}</TableCell>
                  <TableCell>{valor(c.naOperadoraCents)}</TableCell>
                  <TableCell className={cn(c.diferencaCents !== 0 && 'text-destructive')}>{valor(c.diferencaCents)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Rolagem>
      </CardContent>
    </Card>
  )
}

function Resultado({ competencia }: { competencia: string | undefined }) {
  const consulta = useResultadoDoGrupo(competencia)
  const r = consulta.data
  if (consulta.error) return <Erro erro={consulta.error} />
  if (!r) return <Carregando linhas={4} />
  const consolidado = r.linhas.find((l) => l.chave === 'resultado')?.consolidadoCents ?? 0
  const receita = r.linhas.find((l) => l.chave === 'total-da-receita')?.consolidadoCents ?? 0
  const pendencias = r.pendencias.lotesSemCusto + r.pendencias.placasVendidasSemCusto

  return (
    <div className="grid gap-4">
      {r.revendasDoGrupo === 0 && (
        <Aviso>Nenhuma revenda do grupo. Marque em Revendedores.</Aviso>
      )}
      <Indicadores>
        <Indicador rotulo="Resultado consolidado" valor={formatarCentavos(consolidado)} />
        <Indicador rotulo="Receita consolidada" valor={formatarCentavos(receita)} />
        <Indicador rotulo="A reconhecer" valor={formatarCentavos(r.aindaNaoReconhecido.totalCents)} />
        <Indicador rotulo="Pendências de custo" valor={String(pendencias)} />
      </Indicadores>
      {pendencias > 0 && (
        <Aviso>
          {r.pendencias.lotesSemCusto > 0 && `${r.pendencias.lotesSemCusto} ${r.pendencias.lotesSemCusto === 1 ? 'lote despachado' : 'lotes despachados'} sem custo informado. `}
          {r.pendencias.placasVendidasSemCusto > 0 && `${r.pendencias.placasVendidasSemCusto} ${r.pendencias.placasVendidasSemCusto === 1 ? 'placa vendida' : 'placas vendidas'} no mês sem custo de produção. `}
          Informe o custo na aba Custo dos lotes.
        </Aviso>
      )}
      <Demonstrativo resultado={r} />
      <div className="grid items-start gap-4 lg:grid-cols-2">
        <Conciliacao resultado={r} />
        <Card>
          <CardHeader><CardTitle>A reconhecer</CardTitle></CardHeader>
          <CardContent className="grid gap-1 text-sm">
            <p>
              {r.aindaNaoReconhecido.pedidos === 0
                ? 'Nenhum pedido de placas pago está esperando ativação.'
                : `${r.aindaNaoReconhecido.pedidos} ${r.aindaNaoReconhecido.pedidos === 1 ? 'pedido pago' : 'pedidos pagos'} esperando ativação: ${formatarCentavos(r.aindaNaoReconhecido.totalCents)}.`}
            </p>
            {r.aindaNaoReconhecido.daRevendaDoGrupoCents > 0 && (
              <p className="text-muted-foreground">Da revenda do grupo, eliminado no consolidado: {formatarCentavos(r.aindaNaoReconhecido.daRevendaDoGrupoCents)}.</p>
            )}
            <p className="text-muted-foreground">Vira receita quando a revenda ativa o lote.</p>
          </CardContent>
        </Card>
      </div>
    </div>
  )
}

/** O que cada revenda paga à operadora no mês. A revenda de fora do grupo aparece só aqui, nunca com o resultado dela. */
function PorRevenda({ competencia }: { competencia: string | undefined }) {
  const lista = useReceitaPorRevenda(competencia)
  return (
    <Card>
      <CardContent>
        <ListaPaginada lista={lista} textoVazio="Nenhuma revenda pagou à operadora no mês." nomeDaBusca="Buscar nas revendas" placeholder="Nome da revenda">
          {(itens) => (
            <Rolagem>
              <Table semBusca>
                <TableHeader>
                  <TableRow>
                    <TableHead>Revenda</TableHead>
                    <TableHead tipo="numero">Parte da Avalia</TableHead>
                    <TableHead tipo="numero">Pedidos de placas</TableHead>
                    <TableHead tipo="numero">Total</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {itens.map((r) => (
                    <TableRow key={r.revendedorId}>
                      <TableCell className="min-w-40 whitespace-normal">
                        <span className="flex flex-wrap items-center gap-1.5 font-medium">
                          {r.revendedor}
                          {r.doGrupo && <Badge variant="info">Do grupo</Badge>}
                        </span>
                      </TableCell>
                      <TableCell>{valor(r.parteDaAvaliaCents)}</TableCell>
                      <TableCell>{valor(r.pedidosDePlacasCents)}</TableCell>
                      <TableCell>{valor(r.totalCents)}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </Rolagem>
          )}
        </ListaPaginada>
      </CardContent>
    </Card>
  )
}

/**
 * O financeiro da operadora: o resultado consolidado do grupo (a Avalia empresa, revenda do grupo mais operadora),
 * a receita por revenda, as despesas da operadora, o custo de produção por lote e o fechamento do mês do livro dela.
 */
export function TelaDoResultadoDoGrupo() {
  const meses = useMesesDaOperadora()
  const [competencia, setCompetencia] = useState<string | undefined>()
  const resultado = useResultadoDoGrupo(competencia)
  const fechar = useFecharMesDaOperadora()
  const reabrir = useReabrirMesDaOperadora()
  const d = resultado.data
  const mes = competencia ?? d?.competencia

  return (
    <>
      <Cabecalho titulo="Resultados">
        <SeletorDoMes meses={meses.data} atual={d} valor={mes} aoTrocar={setCompetencia} />
        {d && <Fechamento competencia={d.competencia} fechado={d.fechado} fechar={fechar} reabrir={reabrir} />}
      </Cabecalho>
      {d?.fechado && d.fechadoEm && <MesFechado fechadoEm={d.fechadoEm} />}
      <Tabs defaultValue="resultado">
        <TabsList className="mb-3 w-full sm:w-auto">
          <TabsTrigger value="resultado">Resultado</TabsTrigger>
          <TabsTrigger value="revendas">Por revenda</TabsTrigger>
          <TabsTrigger value="despesas">Despesas</TabsTrigger>
          <TabsTrigger value="custos">Custo dos lotes</TabsTrigger>
          <TabsTrigger value="acertar">A acertar</TabsTrigger>
        </TabsList>
        <TabsContent value="resultado"><Resultado competencia={competencia} /></TabsContent>
        <TabsContent value="revendas"><PorRevenda competencia={mes} /></TabsContent>
        <TabsContent value="despesas"><PainelDeDespesasDaOperadora competencia={mes} /></TabsContent>
        <TabsContent value="custos"><PainelDeCustosDeLote /></TabsContent>
        <TabsContent value="acertar"><PainelDeAcertos /></TabsContent>
      </Tabs>
    </>
  )
}
