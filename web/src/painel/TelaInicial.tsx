import { useState } from 'react'
import { Link } from 'react-router'
import { formatarBps, ROTULO_DO_CONTRATO } from '@/api/rotulos'
import { Aviso, Cabecalho, Erro } from '@/componentes/Tela'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { formatarCentavos } from '@/dinheiro/dinheiro'
import { useMinhaRevenda, usePlacas, useVendas } from './consultas'

/** O resumo do dono: contrato, estoque, vendas do mês e o que fazer a seguir. */
export function TelaInicial() {
  const revenda = useMinhaRevenda()
  const placas = usePlacas({ pagina: 1 })
  const vendas = useVendas()
  // Fixado na abertura da tela: o mês não muda entre uma renderização e outra.
  const [inicioDoMes] = useState(() => {
    const hoje = new Date()
    return new Date(hoje.getFullYear(), hoje.getMonth(), 1).getTime()
  })
  const doMes = (vendas.data ?? []).filter((v) => Date.parse(v.vendidaEm) >= inicioDoMes)
  const pagas = doMes.filter((v) => v.pagamento === 'Paga')
  const r = revenda.data?.revendedor
  const porEstado = placas.data?.porEstado ?? {}

  const numeros = [
    { rotulo: 'Placas em estoque', valor: String(porEstado.EmEstoque ?? 0) },
    { rotulo: 'Placas no ar', valor: String((porEstado.Ativa ?? 0) + (porEstado.Carencia ?? 0)) },
    { rotulo: 'Vendas no mês', valor: String(doMes.length) },
    { rotulo: 'Recebido no mês', valor: formatarCentavos(pagas.reduce((s, v) => s + v.valorCents, 0)) },
  ]

  return (
    <>
      <Cabecalho titulo="Início">
        <Button asChild><Link to="/vender">Nova venda</Link></Button>
      </Cabecalho>
      <Erro erro={revenda.error} />
      {r && !r.contratoAssinadoEm && (
        <Aviso>
          Contrato de revenda: {revenda.data?.contrato ? ROTULO_DO_CONTRATO[revenda.data.contrato] : 'sendo preparado'}. Assine pelo e-mail da D4Sign para fazer o primeiro pedido de placas.
        </Aviso>
      )}
      {r && !r.recebedorPagarme && <div className="mt-3"><Aviso>A conta de recebimento no Pagar.me ainda não foi ligada. Sem ela, a venda sai sem cobrança.</Aviso></div>}
      <div className="mt-4 grid grid-cols-2 gap-3 lg:grid-cols-4">
        {numeros.map((n) => (
          <Card key={n.rotulo}>
            <CardHeader><CardTitle className="text-sm font-normal text-muted-foreground">{n.rotulo}</CardTitle></CardHeader>
            <CardContent><p className="text-2xl font-semibold tabular-nums">{n.valor}</p></CardContent>
          </Card>
        ))}
      </div>
      {r && <p className="mt-4 text-sm text-muted-foreground">Comissão padrão da equipe: {formatarBps(r.comissaoPadraoBps)}. Taxa da plataforma: {formatarBps(r.taxaAvaliaBps)} por venda paga.</p>}
    </>
  )
}
