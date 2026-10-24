import { Layers } from 'lucide-react'
import { Link, Navigate, useSearchParams } from 'react-router'
import { mensagemDoErro } from '@/api/cliente'
import { formatarDataHora } from '@/api/rotulos'
import { useSessao } from '@/acesso/sessao'
import { useContaEncerrada } from '@/app/contaEncerrada'
import { Aviso, Cabecalho, Erro, EstadoDaConsulta, Rolagem } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatarCentavos } from '@/dinheiro/dinheiro'
import { AcoesDaVenda } from './AcoesDaVenda'
import { useCobrarDeNovo, useSaldosDeComissao, useVendas } from './consultas'
import { nomeDoProduto, rotuloDaVenda, tomDaVenda } from './situacaoDaVenda'

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
  const cobrar = useCobrarDeNovo()
  const saldo = useSaldosDeComissao()
  const meu = dono ? undefined : saldo.data?.[0]
  const pagas = (vendas.data ?? []).filter((v) => v.pagamento === 'Paga')
  const comissaoPaga = pagas.reduce((s, v) => s + v.comissaoCents, 0)
  // Vendas feitas juntas (mesma cobrança) compartilham o grupo: a lista diz quantas são.
  const porGrupo = (vendas.data ?? []).reduce<Record<string, number>>((mapa, v) => (v.grupoId ? { ...mapa, [v.grupoId]: (mapa[v.grupoId] ?? 0) + 1 } : mapa), {})

  return (
    <>
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
          <EstadoDaConsulta carregando={vendas.isPending} erro={vendas.error} vazio={vendas.data?.length === 0} textoVazio="Nenhuma venda ainda."
            acao={!encerrada && <Button asChild variant="outline"><Link to="/vender">Nova venda</Link></Button>} />
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
                      <TableCell className="min-w-40 whitespace-normal">
                        <Link className="font-medium underline-offset-4 hover:underline" to={`${dono ? '/painel/vendas' : '/vendedor/vendas'}/${v.id}`}>{v.cliente}</Link>
                        <p className="text-xs text-muted-foreground">{nomeDoProduto(v)}</p>
                        {v.grupoId && (
                          <p className="mt-0.5 flex items-center gap-1 text-xs text-muted-foreground">
                            <Layers aria-hidden className="size-3" />
                            {(porGrupo[v.grupoId] ?? 1) > 1 ? `Venda com ${porGrupo[v.grupoId]} itens` : 'Venda com mais itens'}
                          </p>
                        )}
                      </TableCell>
                      <TableCell>{v.codigo ? <Link className="tabular-nums underline-offset-4 hover:underline" to={`/placa/${v.codigo}`}>{v.codigo}</Link> : <span className="text-muted-foreground">-</span>}</TableCell>
                      {dono && <TableCell className="min-w-28 whitespace-normal">{v.vendedor}</TableCell>}
                      <TableCell className="text-right tabular-nums">{formatarCentavos(v.valorCents)}</TableCell>
                      <TableCell className="text-right tabular-nums md:max-xl:hidden">{formatarCentavos(v.comissaoCents)}</TableCell>
                      <TableCell><Badge variant={tomDaVenda(v)}>{rotuloDaVenda(v)}</Badge></TableCell>
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
