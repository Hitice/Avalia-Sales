import { useState } from 'react'
import { mensagemDoErro } from '@/api/cliente'
import { formatarData } from '@/api/rotulos'
import type { LinhaDoResultado, ResultadoDoGrupo } from '@/api/tipos'
import { Indicador, Indicadores } from '@/componentes/Indicador'
import { ListaPaginada } from '@/componentes/ListaPaginada'
import { Aviso, Cabecalho, Carregando, Erro, Rolagem } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Field, FieldLabel } from '@/components/ui/field'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { Textarea } from '@/components/ui/textarea'
import { formatarCentavos } from '@/dinheiro/dinheiro'
import { mesCorrente, nomeDoMes } from '@/financeiro/fatias'
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
      <CardHeader><CardTitle>Resultado do mês</CardTitle></CardHeader>
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
        <Aviso>Nenhuma revenda está marcada como do grupo. Marque em Revendedores; sem isso o consolidado mostra só a operadora.</Aviso>
      )}
      <Indicadores>
        <Indicador rotulo="Resultado consolidado" valor={formatarCentavos(consolidado)} />
        <Indicador rotulo="Receita consolidada" valor={formatarCentavos(receita)} />
        <Indicador rotulo="Recebido e ainda não reconhecido" valor={formatarCentavos(r.aindaNaoReconhecido.totalCents)} />
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
          <CardHeader><CardTitle>Recebido e ainda não reconhecido</CardTitle></CardHeader>
          <CardContent className="grid gap-1 text-sm">
            <p>
              {r.aindaNaoReconhecido.pedidos === 0
                ? 'Nenhum pedido de placas pago está esperando ativação.'
                : `${r.aindaNaoReconhecido.pedidos} ${r.aindaNaoReconhecido.pedidos === 1 ? 'pedido pago' : 'pedidos pagos'} esperando ativação: ${formatarCentavos(r.aindaNaoReconhecido.totalCents)}.`}
            </p>
            {r.aindaNaoReconhecido.daRevendaDoGrupoCents > 0 && (
              <p className="text-muted-foreground">Da revenda do grupo, eliminado no consolidado: {formatarCentavos(r.aindaNaoReconhecido.daRevendaDoGrupoCents)}.</p>
            )}
            <p className="text-muted-foreground">O pedido vira receita da operadora quando a revenda ativa o lote, e não no pagamento.</p>
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
                    <TableHead tipo="numero">Taxa da plataforma</TableHead>
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
                      <TableCell>{valor(r.taxaDaPlataformaCents)}</TableCell>
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

/** Fechar o mês do livro da operadora, com confirmação; reabrir pede o motivo. */
function Fechamento({ competencia, fechado }: { competencia: string; fechado: boolean }) {
  const fechar = useFecharMesDaOperadora()
  const reabrir = useReabrirMesDaOperadora()
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
              <FieldLabel htmlFor="motivo-da-reabertura">Motivo</FieldLabel>
              <Textarea id="motivo-da-reabertura" maxLength={200} value={motivo} onChange={(e) => setMotivo(e.target.value)} />
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
 * O financeiro da operadora: o resultado consolidado do grupo (a Avalia empresa, revenda do grupo mais operadora),
 * a receita por revenda, as despesas da operadora, o custo de produção por lote e o fechamento do mês do livro dela.
 */
export function TelaDoResultadoDoGrupo() {
  const meses = useMesesDaOperadora()
  const [competencia, setCompetencia] = useState<string | undefined>()
  const resultado = useResultadoDoGrupo(competencia)
  const d = resultado.data
  const mes = competencia ?? d?.competencia

  return (
    <>
      <Cabecalho titulo="Resultado do grupo">
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
      {d?.fechado && d.fechadoEm && <p className="mb-3 text-sm text-muted-foreground"><Badge variant="neutro">Fechado</Badge> em {formatarData(d.fechadoEm)}</p>}
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
