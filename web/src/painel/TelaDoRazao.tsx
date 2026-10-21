import { useState } from 'react'
import { formatarDataHora } from '@/api/rotulos'
import type { TipoDeLancamento } from '@/api/tipos'
import { Cabecalho, EstadoDaConsulta, Rolagem } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatarCentavos } from '@/dinheiro/dinheiro'
import { useMesesDoRazao, useRazao } from './consultas'

const ROTULO: Record<TipoDeLancamento, string> = {
  Receita: 'Receita',
  TaxaAvalia: 'Taxa da plataforma',
  ComissaoDevida: 'Comissão devida',
  ComissaoPaga: 'Comissão repassada',
}

const nomeDoMes = (competencia: string) => {
  const [ano, mes] = competencia.split('-').map(Number)
  return new Date(ano, mes - 1, 1).toLocaleDateString('pt-BR', { month: 'long', year: 'numeric' })
}

/** O razão do mês: receita, taxa da plataforma, comissão e o líquido. Estorno é linha nova, nunca apagada. */
export function TelaDoRazao() {
  const meses = useMesesDoRazao()
  const [competencia, setCompetencia] = useState<string | undefined>()
  const razao = useRazao(competencia)
  const r = razao.data
  const totais = r ? [
    { rotulo: 'Receita', valor: r.receitaCents },
    { rotulo: 'Taxa da plataforma', valor: -r.taxaAvaliaCents },
    { rotulo: 'Comissão', valor: -r.comissaoCents },
    { rotulo: 'Líquido', valor: r.liquidoCents },
  ] : []

  return (
    <>
      <Cabecalho titulo="Razão">
        <Select value={competencia ?? r?.competencia ?? ''} onValueChange={setCompetencia}>
          <SelectTrigger aria-label="Mês" className="w-48"><SelectValue placeholder="Mês" /></SelectTrigger>
          <SelectContent>
            {(meses.data?.length ? meses.data : r ? [r.competencia] : []).map((m) => <SelectItem key={m} value={m}>{nomeDoMes(m)}</SelectItem>)}
          </SelectContent>
        </Select>
      </Cabecalho>
      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        {totais.map((t) => (
          <Card key={t.rotulo}>
            <CardHeader><CardTitle className="text-sm font-normal text-muted-foreground">{t.rotulo}</CardTitle></CardHeader>
            <CardContent><p className="text-2xl font-semibold tabular-nums">{formatarCentavos(t.valor)}</p></CardContent>
          </Card>
        ))}
      </div>
      <p className="mt-2 text-xs text-muted-foreground">A taxa do Pagar.me sai no extrato do Pagar.me.</p>
      <Card className="mt-4">
        <CardContent>
          <EstadoDaConsulta carregando={razao.isPending} erro={razao.error} vazio={r?.lancamentos.length === 0} textoVazio="Nenhum lançamento neste mês." />
          {r && r.lancamentos.length > 0 && (
            <Rolagem>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Data</TableHead>
                    <TableHead>Tipo</TableHead>
                    <TableHead>Descrição</TableHead>
                    <TableHead className="text-right">Valor</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {r.lancamentos.map((l) => (
                    <TableRow key={l.id}>
                      <TableCell>{formatarDataHora(l.ocorridoEm)}</TableCell>
                      <TableCell>{ROTULO[l.tipo]} {l.estorno && <Badge variant="destructive">Estorno</Badge>}</TableCell>
                      <TableCell>{l.descricao}</TableCell>
                      <TableCell className={`text-right tabular-nums ${l.valorCents < 0 ? 'text-muted-foreground' : ''}`}>{formatarCentavos(l.valorCents)}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </Rolagem>
          )}
        </CardContent>
      </Card>
    </>
  )
}
