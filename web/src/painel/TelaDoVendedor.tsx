import { useMemo, useState } from 'react'
import { Link } from 'react-router'
import { formatarData } from '@/api/rotulos'
import { useSessao } from '@/acesso/sessao'
import { Cabecalho, Carregando, Erro, Vazio } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardAction, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { formatarCentavos } from '@/dinheiro/dinheiro'
import { usePainelDoVendedor, usePlacas, useResumoDasVendas, useSaldosDeComissao, useVendasRecentes } from './consultas'
import { Indicador, Indicadores } from '@/componentes/Indicador'
import { rotuloDaVenda, tomDaVenda } from './situacaoDaVenda'
import { porDiaDe, VendasEm30Dias } from './VendasEm30Dias'
import { Rosca } from '@/financeiro/Graficos'

/**
 * O painel do vendedor: o que vendeu no mês, a comissão a receber (venda paga,
 * ainda não repassada), a que espera o cliente pagar e a já recebida. A API só
 * devolve as vendas e o saldo dele.
 */
export function TelaDoVendedor() {
  const nome = useSessao((s) => s.perfil?.nome)
  const recentes = useVendasRecentes()
  const resumo = useResumoDasVendas()
  const saldos = useSaldosDeComissao()
  const painel = usePainelDoVendedor()
  const placas = usePlacas({ pagina: 1, tamanho: 1 })
  const validas = (recentes.data?.itens ?? []).filter((v) => v.situacao !== 'Cancelada')
  const r = resumo.data
  const saldo = saldos.data?.[0]
  // Fixado na abertura da tela: o dia não muda entre uma renderização e outra.
  const [agora] = useState(() => Date.now())
  const porDia = useMemo(() => porDiaDe((r?.porDia ?? []).map((d) => ({ dia: d.dia, total: d.total })), agora), [r, agora])

  const numeros = [
    { rotulo: 'Vendas no mês', valor: String(r?.noMes ?? 0) },
    { rotulo: 'Vendido no mês', valor: formatarCentavos(r?.recebidoNoMesCents ?? 0) },
    { rotulo: 'Comissão a receber', valor: formatarCentavos(saldo?.abertaCents ?? 0), destaque: true },
    { rotulo: 'A pagar pelo cliente', valor: formatarCentavos(r?.comissaoEsperandoCents ?? 0) },
    { rotulo: 'Comissão recebida', valor: formatarCentavos(saldo?.repassadaCents ?? 0) },
    { rotulo: 'Placas comigo', valor: String(placas.data?.porEstado.EmEstoque ?? 0) },
  ]

  return (
    <>
      <Cabecalho titulo={nome ? `Olá, ${nome.split(' ')[0]}` : 'Início'}>
        <Button asChild className="lg:hidden"><Link to="/vender">Nova venda</Link></Button>
      </Cabecalho>
      <Erro erro={recentes.error ?? resumo.error ?? saldos.error} />
      <Indicadores colunas={3}>
        {numeros.map((n) => <Indicador key={n.rotulo} rotulo={n.rotulo} valor={n.valor} destaque={n.destaque} />)}
      </Indicadores>
      {saldo?.ultimoRepasse && <p className="mt-3 text-xs text-muted-foreground">Último repasse em {formatarData(saldo.ultimoRepasse)}.</p>}
      {painel.data && (
        <div className="mt-3 grid gap-3 md:mt-4 md:gap-4 lg:grid-cols-12">
          <MetaDoMes className="lg:col-span-7" metaCents={painel.data.metaMensalCents} vendidoCents={painel.data.vendidoNoMesCents} />
          <Ranking className="lg:col-span-5" linhas={painel.data.ranking} />
        </div>
      )}
      <div className="mt-3 grid gap-3 md:mt-4 md:gap-4 lg:grid-cols-12">
        <VendasEm30Dias className="lg:col-span-7" porDia={porDia} carregando={resumo.isPending} />
        <Rosca className="lg:col-span-5" titulo="Minha comissão" vazio="Nenhuma comissão ainda."
          itens={[
            { nome: 'A receber', valorCents: Math.max(0, saldo?.abertaCents ?? 0) },
            { nome: 'Esperando o cliente pagar', valorCents: r?.comissaoEsperandoCents ?? 0 },
            { nome: 'Recebida', valorCents: saldo?.repassadaCents ?? 0 },
          ]} />
      </div>
      <Card className="mt-3 md:mt-4">
        <CardHeader>
          <CardTitle>Últimas vendas</CardTitle>
          {(r?.total ?? 0) > 5 && <CardAction><Button asChild variant="link" size="sm" className="-mr-2.5"><Link to="/vendedor/vendas">Ver todas</Link></Button></CardAction>}
        </CardHeader>
        <CardContent>
          {recentes.isPending ? <Carregando linhas={3} /> : validas.length === 0
            ? <Vazio texto="Nenhuma venda ainda." acao={<Button asChild variant="outline"><Link to="/vender">Nova venda</Link></Button>} />
            : (
                <ul className="-my-1 divide-y">
                  {validas.slice(0, 5).map((v) => (
                    <li key={v.id}>
                      <Link to={`/vendedor/vendas/${v.id}`} className="-mx-2 flex min-h-14 items-center justify-between gap-3 rounded-md px-2 py-2.5 transition-colors duration-150 hover:bg-cinza-50">
                        <span className="min-w-0">
                          <span className="block truncate font-medium">{v.cliente}</span>
                          <span className="block truncate text-xs text-muted-foreground tabular-nums">{formatarData(v.vendidaEm)}, comissão {formatarCentavos(v.comissaoCents)}</span>
                        </span>
                        <Badge variant={tomDaVenda(v)}>{rotuloDaVenda(v)}</Badge>
                      </Link>
                    </li>
                  ))}
                </ul>
              )}
        </CardContent>
      </Card>
    </>
  )
}

/** A meta que o revendedor definiu e o quanto das vendas pagas do mês já chegou nela. */
function MetaDoMes({ metaCents, vendidoCents, className }: { metaCents: number | null; vendidoCents: number; className?: string }) {
  const parte = metaCents ? Math.min(100, Math.round((vendidoCents / metaCents) * 100)) : 0
  return (
    <Card className={className}>
      <CardHeader><CardTitle>Meta do mês</CardTitle></CardHeader>
      <CardContent className="grid gap-3">
        {metaCents
          ? (
              <>
                <p className="flex items-baseline justify-between gap-3 text-sm">
                  <span><span className="text-lg font-semibold tabular-nums">{formatarCentavos(vendidoCents)}</span> de {formatarCentavos(metaCents)}</span>
                  <span className="font-medium tabular-nums">{parte}%</span>
                </p>
                <div className="h-2 overflow-hidden rounded-full bg-muted" role="progressbar" aria-label="Meta do mês" aria-valuemin={0} aria-valuemax={100} aria-valuenow={parte}>
                  <div className="h-full rounded-full bg-primary transition-all duration-500" style={{ width: `${parte}%` }} />
                </div>
                <p className="text-xs text-muted-foreground">{vendidoCents >= metaCents ? 'Meta batida.' : `Faltam ${formatarCentavos(metaCents - vendidoCents)} em vendas pagas.`}</p>
              </>
            )
          : <p className="text-sm text-muted-foreground">Sem meta neste mês. Vendido e pago: <span className="font-medium text-foreground tabular-nums">{formatarCentavos(vendidoCents)}</span>.</p>}
      </CardContent>
    </Card>
  )
}

/** A posição de cada um pelas vendas pagas do mês. Valor e comissão dos outros não aparecem. */
function Ranking({ linhas, className }: { linhas: { posicao: number; nome: string; vendas: number; voce: boolean }[]; className?: string }) {
  return (
    <Card className={className}>
      <CardHeader><CardTitle>Ranking do mês</CardTitle></CardHeader>
      <CardContent>
        {linhas.length === 0 ? <p className="text-sm text-muted-foreground">A equipe ainda não tem vendedores.</p> : (
          <ol className="grid gap-1.5 text-sm">
            {linhas.slice(0, 5).map((l) => (
              <li key={l.posicao} className={`flex items-center gap-3 rounded-md px-2 py-1.5 ${l.voce ? 'bg-accent font-medium' : ''}`}>
                <span className="w-5 text-right text-muted-foreground tabular-nums">{l.posicao}º</span>
                <span className="min-w-0 flex-1 truncate">{l.nome}{l.voce ? ' (você)' : ''}</span>
                <span className="tabular-nums">{l.vendas} {l.vendas === 1 ? 'paga' : 'pagas'}</span>
              </li>
            ))}
            {!linhas.slice(0, 5).some((l) => l.voce) && linhas.find((l) => l.voce) && (
              <li className="flex items-center gap-3 rounded-md bg-accent px-2 py-1.5 font-medium">
                <span className="w-5 text-right text-muted-foreground tabular-nums">{linhas.find((l) => l.voce)!.posicao}º</span>
                <span className="min-w-0 flex-1 truncate">Você</span>
                <span className="tabular-nums">{linhas.find((l) => l.voce)!.vendas} pagas</span>
              </li>
            )}
          </ol>
        )}
      </CardContent>
    </Card>
  )
}
