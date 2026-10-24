import { useMemo, useState } from 'react'
import { Link } from 'react-router'
import { formatarData } from '@/api/rotulos'
import { useSessao } from '@/acesso/sessao'
import { Cabecalho, Carregando, Erro, Vazio } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardAction, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { formatarCentavos } from '@/dinheiro/dinheiro'
import { usePlacas, useSaldosDeComissao, useVendas } from './consultas'
import { Indicador, Indicadores } from '@/componentes/Indicador'
import { rotuloDaVenda, tomDaVenda } from './situacaoDaVenda'
import { VendasEm30Dias, vendasPorDia } from './VendasEm30Dias'
import { Rosca } from '@/financeiro/Graficos'

/**
 * O painel do vendedor: o que vendeu no mês, a comissão a receber (venda paga,
 * ainda não repassada), a que espera o cliente pagar e a já recebida. A API só
 * devolve as vendas e o saldo dele.
 */
export function TelaDoVendedor() {
  const nome = useSessao((s) => s.perfil?.nome)
  const vendas = useVendas()
  const saldos = useSaldosDeComissao()
  const placas = usePlacas({ pagina: 1 })
  // Fixado na abertura da tela: o mês não muda entre uma renderização e outra.
  const [inicioDoMes] = useState(() => {
    const hoje = new Date()
    return new Date(hoje.getFullYear(), hoje.getMonth(), 1).getTime()
  })
  const validas = (vendas.data ?? []).filter((v) => v.situacao !== 'Cancelada')
  const doMes = validas.filter((v) => Date.parse(v.vendidaEm) >= inicioDoMes)
  const esperando = validas.filter((v) => v.pagamento !== 'Paga' && !v.cortesia && v.comissaoCents > 0)
  const saldo = saldos.data?.[0]
  const [agora] = useState(() => Date.now())
  const porDia = useMemo(() => vendasPorDia(vendas.data ?? [], agora), [vendas.data, agora])
  const soma = (lista: typeof validas, campo: 'valorCents' | 'comissaoCents') => lista.reduce((s, v) => s + v[campo], 0)

  const numeros = [
    { rotulo: 'Vendas no mês', valor: String(doMes.length) },
    { rotulo: 'Vendido no mês', valor: formatarCentavos(soma(doMes.filter((v) => v.pagamento === 'Paga'), 'valorCents')) },
    { rotulo: 'Comissão a receber', valor: formatarCentavos(saldo?.abertaCents ?? 0), destaque: true },
    { rotulo: 'A pagar pelo cliente', valor: formatarCentavos(soma(esperando, 'comissaoCents')) },
    { rotulo: 'Comissão recebida', valor: formatarCentavos(saldo?.repassadaCents ?? 0) },
    { rotulo: 'Placas comigo', valor: String(placas.data?.porEstado.EmEstoque ?? 0) },
  ]

  return (
    <>
      <Cabecalho titulo={nome ? `Olá, ${nome.split(' ')[0]}` : 'Início'}>
        <Button asChild className="lg:hidden"><Link to="/vender">Nova venda</Link></Button>
      </Cabecalho>
      <Erro erro={vendas.error ?? saldos.error} />
      <Indicadores colunas={3}>
        {numeros.map((n) => <Indicador key={n.rotulo} rotulo={n.rotulo} valor={n.valor} destaque={n.destaque} />)}
      </Indicadores>
      {saldo?.ultimoRepasse && <p className="mt-3 text-xs text-muted-foreground">Último repasse em {formatarData(saldo.ultimoRepasse)}.</p>}
      <div className="mt-3 grid gap-3 md:mt-4 md:gap-4 lg:grid-cols-12">
        <VendasEm30Dias className="lg:col-span-7" porDia={porDia} carregando={vendas.isPending} />
        <Rosca className="lg:col-span-5" titulo="Minha comissão" vazio="Nenhuma comissão ainda."
          itens={[
            { nome: 'A receber', valorCents: Math.max(0, saldo?.abertaCents ?? 0) },
            { nome: 'Esperando o cliente pagar', valorCents: soma(esperando, 'comissaoCents') },
            { nome: 'Recebida', valorCents: saldo?.repassadaCents ?? 0 },
          ]} />
      </div>
      <Card className="mt-3 md:mt-4">
        <CardHeader>
          <CardTitle>Últimas vendas</CardTitle>
          {validas.length > 5 && <CardAction><Button asChild variant="link" size="sm" className="-my-2 -mr-2.5"><Link to="/vendedor/vendas">Ver todas</Link></Button></CardAction>}
        </CardHeader>
        <CardContent>
          {vendas.isPending ? <Carregando linhas={3} /> : validas.length === 0
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
