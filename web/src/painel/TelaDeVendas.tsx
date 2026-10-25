import { Layers } from 'lucide-react'
import { useState } from 'react'
import { Link, Navigate, useSearchParams } from 'react-router'
import { mensagemDoErro } from '@/api/cliente'
import { formatarDataHora } from '@/api/rotulos'
import { useSessao } from '@/acesso/sessao'
import { useContaEncerrada } from '@/app/contaEncerrada'
import { AcoesDaLinha } from '@/componentes/AcoesDaLinha'
import { ListaPaginada } from '@/componentes/ListaPaginada'
import { Aviso, Cabecalho, Erro, Rolagem } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatarCentavos } from '@/dinheiro/dinheiro'
import { AcoesDaVenda } from './AcoesDaVenda'
import { useCobrarDeNovo, useResumoDasVendas, useSaldosDeComissao, useVendas } from './consultas'
import { nomeDoProduto, podeGerarCobranca, rotuloDaVenda, temAcoesDaVenda, tomDaVenda } from './situacaoDaVenda'

/**
 * As vendas e a comissão de cada uma (a comissão só conta quando o cliente
 * paga). O Catálogo tem tela própria no menu.
 */
export function TelaDeVendas() {
  const dono = useSessao((s) => s.perfil?.papel === 'Dono')
  const encerrada = useContaEncerrada()
  // O Catálogo era uma aba daqui: o endereço antigo leva à tela dele.
  if (useSearchParams()[0].get('aba') === 'catalogo') return <Navigate to="/catalogo" replace />

  return (
    <>
      <Cabecalho titulo={dono ? 'Vendas' : 'Minhas vendas'}>
        {!encerrada && <Button asChild className="lg:hidden"><Link to="/vender">Nova venda</Link></Button>}
      </Cabecalho>
      <ListaDeVendas dono={dono} />
    </>
  )
}

function ListaDeVendas({ dono }: { dono: boolean }) {
  const encerrada = useContaEncerrada()
  const vendas = useVendas()
  const resumo = useResumoDasVendas().data
  const cobrar = useCobrarDeNovo()
  const saldo = useSaldosDeComissao()
  const meu = dono ? undefined : saldo.data?.[0]
  const [editando, setEditando] = useState<string | null>(null)
  const editada = vendas.itens?.find((v) => v.id === editando)

  return (
    <>
      <Erro erro={cobrar.error ? mensagemDoErro(cobrar.error) : null} />
      {cobrar.data?.semCobranca && <Aviso>{cobrar.data.semCobranca}</Aviso>}
      {meu && (
        <p className="mb-3 text-sm">
          Comissão a receber: <span className="font-semibold tabular-nums">{formatarCentavos(meu.abertaCents)}</span>. Já recebida: <span className="tabular-nums">{formatarCentavos(meu.repassadaCents)}</span>.
        </p>
      )}
      {resumo && resumo.total > 0 && (
        <p className="mb-3 text-sm text-muted-foreground">
          {resumo.pagas} de {resumo.total} vendas pagas. Comissão das pagas: <span className="font-medium text-foreground tabular-nums">{formatarCentavos(resumo.comissaoPagaCents)}</span>
        </p>
      )}
      <Card>
        <CardContent>
          <ListaPaginada lista={vendas} textoVazio="Nenhuma venda ainda." nomeDaBusca="Buscar nas vendas" placeholder="Cliente, placa, produto ou vendedor"
            acaoVazia={!encerrada && <Button asChild variant="outline"><Link to="/vender">Nova venda</Link></Button>}>
            {(itens) => (
              <Rolagem>
                <Table semBusca>
                  <TableHeader>
                    <TableRow>
                      <TableHead tipo="data">Data</TableHead>
                      <TableHead>Cliente</TableHead>
                      <TableHead>Placa</TableHead>
                      {dono && <TableHead>Vendedor</TableHead>}
                      <TableHead tipo="numero">Valor</TableHead>
                      <TableHead tipo="numero" className="md:max-xl:hidden">Comissão</TableHead>
                      <TableHead tipo="situacao">Pagamento</TableHead>
                      <TableHead tipo="acoes" />
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {itens.map((v) => (
                      <TableRow key={v.id}>
                        <TableCell>{formatarDataHora(v.vendidaEm)}</TableCell>
                        <TableCell className="min-w-40 whitespace-normal">
                          <Link className="font-medium underline-offset-4 hover:underline" to={`${dono ? '/painel/vendas' : '/vendedor/vendas'}/${v.id}`}>{v.cliente}</Link>
                          <p className="text-xs text-muted-foreground">{nomeDoProduto(v)}</p>
                          {v.grupoId && (
                            <p className="mt-0.5 flex items-center gap-1 text-xs text-muted-foreground">
                              <Layers aria-hidden className="size-3" />
                              {v.itensNoGrupo > 1 ? `Venda com ${v.itensNoGrupo} itens` : 'Venda com mais itens'}
                            </p>
                          )}
                        </TableCell>
                        <TableCell>{v.codigo ? <Link className="tabular-nums underline-offset-4 hover:underline" to={`/placa/${v.codigo}`}>{v.codigo}</Link> : <span className="text-muted-foreground">-</span>}</TableCell>
                        {dono && <TableCell className="min-w-28 whitespace-normal">{v.vendedor}</TableCell>}
                        <TableCell>{formatarCentavos(v.valorCents)}</TableCell>
                        <TableCell className="md:max-xl:hidden">{formatarCentavos(v.comissaoCents)}</TableCell>
                        <TableCell><Badge variant={tomDaVenda(v)}>{rotuloDaVenda(v)}</Badge></TableCell>
                        <TableCell>
                          <AcoesDaLinha nome={`venda de ${v.cliente}`}
                            principal={podeGerarCobranca(v) && { rotulo: 'Gerar cobrança', desabilitada: cobrar.isPending, aoClicar: () => cobrar.mutate(v.id) }}
                            outras={[
                              { rotulo: 'Abrir a venda', para: `${dono ? '/painel/vendas' : '/vendedor/vendas'}/${v.id}` },
                              temAcoesDaVenda(v, dono) && { rotulo: 'Editar ou cancelar', aoClicar: () => setEditando(v.id) },
                            ]} />
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </Rolagem>
            )}
          </ListaPaginada>
        </CardContent>
      </Card>
      {editada && <AcoesDaVenda venda={editada} dono={dono} aoFechar={() => setEditando(null)} />}
    </>
  )
}
