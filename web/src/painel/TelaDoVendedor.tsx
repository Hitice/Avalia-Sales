import { useState } from 'react'
import { Link } from 'react-router'
import { ROTULO_DO_PAGAMENTO, formatarData } from '@/api/rotulos'
import { useSessao } from '@/acesso/sessao'
import { Cabecalho, Erro } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { formatarCentavos } from '@/dinheiro/dinheiro'
import { usePlacas, useSaldosDeComissao, useVendas } from './consultas'

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
  const soma = (lista: typeof validas, campo: 'valorCents' | 'comissaoCents') => lista.reduce((s, v) => s + v[campo], 0)

  const numeros = [
    { rotulo: 'Vendas no mês', valor: String(doMes.length) },
    { rotulo: 'Vendido no mês', valor: formatarCentavos(soma(doMes.filter((v) => v.pagamento === 'Paga'), 'valorCents')) },
    { rotulo: 'Comissão a receber', valor: formatarCentavos(saldo?.abertaCents ?? 0), destaque: true },
    { rotulo: 'Esperando o cliente pagar', valor: formatarCentavos(soma(esperando, 'comissaoCents')) },
    { rotulo: 'Comissão recebida', valor: formatarCentavos(saldo?.repassadaCents ?? 0) },
    { rotulo: 'Placas comigo', valor: String(placas.data?.porEstado.EmEstoque ?? 0) },
  ]

  return (
    <>
      <Cabecalho titulo={nome ? `Olá, ${nome.split(' ')[0]}` : 'Início'}>
        <Button asChild><Link to="/vender">Nova venda</Link></Button>
      </Cabecalho>
      <Erro erro={vendas.error ?? saldos.error} />
      <div className="grid grid-cols-2 gap-3 lg:grid-cols-3">
        {numeros.map((n) => (
          <Card key={n.rotulo} className={n.destaque ? 'ring-2 ring-sucesso' : undefined}>
            <CardHeader><CardTitle className="text-sm font-normal text-muted-foreground">{n.rotulo}</CardTitle></CardHeader>
            <CardContent><p className="text-2xl font-semibold tabular-nums">{n.valor}</p></CardContent>
          </Card>
        ))}
      </div>
      {saldo?.ultimoRepasse && <p className="mt-3 text-sm text-muted-foreground">Último repasse em {formatarData(saldo.ultimoRepasse)}.</p>}
      <Card className="mt-4">
        <CardHeader><CardTitle>Últimas vendas</CardTitle></CardHeader>
        <CardContent className="grid gap-2">
          {validas.length === 0 && <p className="text-sm text-muted-foreground">Nenhuma venda ainda.</p>}
          {validas.slice(0, 5).map((v) => (
            <Link key={v.id} to={`/placa/${v.codigo}`} className="flex items-center justify-between gap-3 rounded-lg border p-3 text-sm hover:bg-muted">
              <span className="min-w-0">
                <span className="block truncate font-medium">{v.cliente}</span>
                <span className="text-muted-foreground">{formatarData(v.vendidaEm)} - comissão {formatarCentavos(v.comissaoCents)}</span>
              </span>
              <Badge variant={v.pagamento === 'Paga' ? 'default' : 'outline'}>{v.pagamento ? ROTULO_DO_PAGAMENTO[v.pagamento] : 'Sem cobrança'}</Badge>
            </Link>
          ))}
          {validas.length > 5 && <Button variant="ghost" asChild><Link to="/vendedor/vendas">Ver todas</Link></Button>}
        </CardContent>
      </Card>
    </>
  )
}
