import { Link } from 'react-router'
import { mensagemDoErro } from '@/api/cliente'
import { formatarDataHora, ROTULO_DO_PAGAMENTO } from '@/api/rotulos'
import { useSessao } from '@/acesso/sessao'
import { Aviso, Cabecalho, Erro, EstadoDaConsulta, Rolagem } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import type { VendaDaLista } from '@/api/tipos'
import { formatarCentavos } from '@/dinheiro/dinheiro'
import { AcoesDaVenda } from './AcoesDaVenda'
import { useCobrarDeNovo, useSaldosDeComissao, useVendas } from './consultas'

function rotuloDoPagamento(v: VendaDaLista) {
  if (v.situacao === 'Cancelada') return 'Venda cancelada'
  if (v.cortesia) return 'Cortesia'
  if (v.doSistemaAnterior) return 'Paga no sistema anterior'
  if (v.pagamento === 'Paga' && v.pagoPor === 'Dinheiro') return 'Paga em dinheiro'
  if (v.pagamento === 'Paga' && v.pagoPor === 'OutroPix') return 'Paga por outro Pix'
  return v.pagamento ? ROTULO_DO_PAGAMENTO[v.pagamento] : 'Sem cobrança'
}

/** As vendas e a comissão de cada uma. A comissão só conta quando o cliente paga. */
export function TelaDeVendas() {
  const dono = useSessao((s) => s.perfil?.papel === 'Dono')
  const vendas = useVendas()
  const cobrar = useCobrarDeNovo()
  const saldo = useSaldosDeComissao()
  const meu = dono ? undefined : saldo.data?.[0]
  const pagas = (vendas.data ?? []).filter((v) => v.pagamento === 'Paga')
  const comissaoPaga = pagas.reduce((s, v) => s + v.comissaoCents, 0)

  return (
    <>
      <Cabecalho titulo={dono ? 'Vendas' : 'Minhas vendas'}>
        <Button asChild><Link to="/vender">Nova venda</Link></Button>
      </Cabecalho>
      <Erro erro={cobrar.error ? mensagemDoErro(cobrar.error) : null} />
      {cobrar.data?.semCobranca && <Aviso>{cobrar.data.semCobranca}</Aviso>}
      {meu && (
        <p className="mb-3 text-sm">
          Comissão a receber: <span className="font-semibold tabular-nums">{formatarCentavos(meu.abertaCents)}</span>. Já recebida: <span className="tabular-nums">{formatarCentavos(meu.repassadaCents)}</span>.
        </p>
      )}
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
                    <TableHead className="text-right md:max-xl:hidden">Comissão</TableHead>
                    <TableHead>Pagamento</TableHead>
                    <TableHead><span className="sr-only">Ação</span></TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {vendas.data.map((v) => (
                    <TableRow key={v.id}>
                      <TableCell>{formatarDataHora(v.vendidaEm)}</TableCell>
                      <TableCell className="min-w-40 whitespace-normal"><p className="font-medium">{v.cliente}</p><p className="text-xs text-muted-foreground">{v.produto}</p></TableCell>
                      <TableCell><Link className="tabular-nums underline-offset-4 hover:underline" to={`/placa/${v.codigo}`}>{v.codigo}</Link></TableCell>
                      {dono && <TableCell className="min-w-28 whitespace-normal">{v.vendedor}</TableCell>}
                      <TableCell className="text-right tabular-nums">{formatarCentavos(v.valorCents)}</TableCell>
                      <TableCell className="text-right tabular-nums md:max-xl:hidden">{formatarCentavos(v.comissaoCents)}</TableCell>
                      <TableCell><Badge variant={v.situacao === 'Cancelada' ? 'outline' : v.pagamento === 'Paga' ? 'default' : 'secondary'}>{rotuloDoPagamento(v)}</Badge></TableCell>
                      <TableCell>
                        <div className="flex items-center justify-end gap-2">
                          {v.situacao === 'Registrada' && !v.cortesia && !v.doSistemaAnterior && (v.pagamento === null || v.pagamento === 'Falhou' || v.pagamento === 'Cancelada') && (
                            <Button variant="outline" disabled={cobrar.isPending} onClick={() => cobrar.mutate(v.id)}>Gerar cobrança</Button>
                          )}
                          <AcoesDaVenda venda={v} dono={dono} />
                        </div>
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
