import { Link } from 'react-router'
import { mensagemDoErro } from '@/api/cliente'
import { formatarDataHora, ROTULO_DO_PAGAMENTO } from '@/api/rotulos'
import { useSessao } from '@/acesso/sessao'
import { Aviso, Cabecalho, Erro, EstadoDaConsulta, Rolagem } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatarCentavos } from '@/dinheiro/dinheiro'
import { useCobrarDeNovo, useVendas } from './consultas'

/** As vendas e a comissão de cada uma. A comissão só conta quando o cliente paga. */
export function TelaDeVendas() {
  const dono = useSessao((s) => s.perfil?.papel === 'Dono')
  const vendas = useVendas()
  const cobrar = useCobrarDeNovo()
  const pagas = (vendas.data ?? []).filter((v) => v.pagamento === 'Paga')
  const comissaoPaga = pagas.reduce((s, v) => s + v.comissaoCents, 0)

  return (
    <>
      <Cabecalho titulo={dono ? 'Vendas' : 'Minhas vendas'}>
        <Button asChild><Link to="/vender">Nova venda</Link></Button>
      </Cabecalho>
      <Erro erro={cobrar.error ? mensagemDoErro(cobrar.error) : null} />
      {cobrar.data?.semCobranca && <Aviso>{cobrar.data.semCobranca}</Aviso>}
      {vendas.data && vendas.data.length > 0 && (
        <p className="mb-3 text-sm text-muted-foreground">
          {pagas.length} de {vendas.data.length} vendas pagas. Comissão das pagas: <span className="font-medium text-foreground tabular-nums">{formatarCentavos(comissaoPaga)}</span>
        </p>
      )}
      <Card>
        <CardContent>
          <EstadoDaConsulta carregando={vendas.isPending} erro={vendas.error} vazio={vendas.data?.length === 0} textoVazio="Nenhuma venda ainda." />
          {vendas.data && vendas.data.length > 0 && (
            <Rolagem>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Data</TableHead>
                    <TableHead>Cliente</TableHead>
                    <TableHead>Placa</TableHead>
                    {dono && <TableHead>Vendedor</TableHead>}
                    <TableHead className="text-right">Valor</TableHead>
                    <TableHead className="text-right">Comissão</TableHead>
                    <TableHead>Pagamento</TableHead>
                    <TableHead><span className="sr-only">Ação</span></TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {vendas.data.map((v) => (
                    <TableRow key={v.id}>
                      <TableCell>{formatarDataHora(v.vendidaEm)}</TableCell>
                      <TableCell><p className="font-medium">{v.cliente}</p><p className="text-xs text-muted-foreground">{v.produto}</p></TableCell>
                      <TableCell><Link className="tabular-nums underline-offset-4 hover:underline" to={`/placa/${v.codigo}`}>{v.codigo}</Link></TableCell>
                      {dono && <TableCell>{v.vendedor}</TableCell>}
                      <TableCell className="text-right tabular-nums">{formatarCentavos(v.valorCents)}</TableCell>
                      <TableCell className="text-right tabular-nums">{formatarCentavos(v.comissaoCents)}</TableCell>
                      <TableCell><Badge variant={v.pagamento === 'Paga' ? 'default' : 'secondary'}>{v.pagamento ? ROTULO_DO_PAGAMENTO[v.pagamento] : 'Sem cobrança'}</Badge></TableCell>
                      <TableCell className="text-right">
                        {(v.pagamento === null || v.pagamento === 'Falhou' || v.pagamento === 'Cancelada') && (
                          <Button variant="outline" disabled={cobrar.isPending} onClick={() => cobrar.mutate(v.id)}>Gerar cobrança</Button>
                        )}
                      </TableCell>
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
