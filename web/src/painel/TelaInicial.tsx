import { useState } from 'react'
import { Link } from 'react-router'
import { formatarBps, paraBps, ROTULO_DO_CONTRATO } from '@/api/rotulos'
import { Aviso, Cabecalho, Erro } from '@/componentes/Tela'
import { Button } from '@/components/ui/button'
import { formatarCentavos } from '@/dinheiro/dinheiro'
import { useDefinirCelular, useDefinirRenovacao, useMinhaRevenda, usePlacas, useVendas } from './consultas'
import { Input } from '@/components/ui/input'
import { digitos, mascaraTelefone } from '@/lib/mascaras'
import { Indicador, Indicadores } from '@/componentes/Indicador'

/** O resumo do dono: contrato, estoque, vendas do mês e o que fazer a seguir. */
export function TelaInicial() {
  const revenda = useMinhaRevenda()
  const placas = usePlacas({ pagina: 1 })
  const vendas = useVendas()
  const definirRenovacao = useDefinirRenovacao()
  const [renovacao, setRenovacao] = useState('')
  const definirCelular = useDefinirCelular()
  const [celular, setCelular] = useState('')
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
          Contrato de revenda: {revenda.data?.contrato ? ROTULO_DO_CONTRATO[revenda.data.contrato].toLowerCase() : 'em preparo'}. Assine pelo e-mail da D4Sign.
        </Aviso>
      )}
      {r && !r.celular && <div className="mt-3"><Aviso>Informe o celular de contato da revenda.</Aviso></div>}
      {r && !r.recebedorPagarme && <div className="mt-3"><Aviso>Conta de recebimento no Pagar.me pendente.</Aviso></div>}
      <div className="mt-4">
        <Indicadores>{numeros.map((n) => <Indicador key={n.rotulo} rotulo={n.rotulo} valor={n.valor} />)}</Indicadores>
      </div>
      {r && <p className="mt-4 text-sm text-muted-foreground">Comissão padrão: {formatarBps(r.comissaoPadraoBps)}. Taxa da plataforma: {formatarBps(r.taxaAvaliaBps)}.</p>}
      {r && (
        <form
          className="mt-3 flex flex-wrap items-center gap-2 text-sm"
          onSubmit={(e) => {
            e.preventDefault()
            const bps = paraBps(renovacao)
            if (bps !== null) definirRenovacao.mutate(bps, { onSuccess: () => setRenovacao('') })
          }}
        >
          <label htmlFor="renovacao">Renovação (% da venda)</label>
          <Input id="renovacao" inputMode="decimal" className="w-24" placeholder={formatarBps(r.renovacaoBps).replace('%', '')} value={renovacao} onChange={(e) => setRenovacao(e.target.value)} />
          <Button type="submit" variant="outline" disabled={paraBps(renovacao) === null}>Salvar</Button>
          {definirRenovacao.error && <span className="text-destructive">{definirRenovacao.error.message}</span>}
        </form>
      )}
      {r && (
        <form
          className="mt-3 flex flex-wrap items-center gap-2 text-sm"
          onSubmit={(e) => {
            e.preventDefault()
            definirCelular.mutate(digitos(celular), { onSuccess: () => setCelular('') })
          }}
        >
          <label htmlFor="celular">Celular de contato</label>
          <Input id="celular" type="tel" inputMode="numeric" className="w-40" placeholder={r.celular ? mascaraTelefone(r.celular) : '(00) 00000-0000'} value={celular} onChange={(e) => setCelular(mascaraTelefone(e.target.value))} />
          <Button type="submit" variant="outline" disabled={digitos(celular).length < 10}>Salvar</Button>
          {definirCelular.error && <span className="text-destructive">{definirCelular.error.message}</span>}
        </form>
      )}
    </>
  )
}
