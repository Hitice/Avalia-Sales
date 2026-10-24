import { ChevronLeft } from 'lucide-react'
import { Link, useParams } from 'react-router'
import { mensagemDoErro } from '@/api/cliente'
import { formatarData, formatarDataHora, formatarTelefone, ROTULO_DA_ORDEM, ROTULO_DO_CANCELAMENTO, ROTULO_DO_LANCAMENTO, ROTULO_DO_MEIO, ROTULO_DO_PAGAMENTO, TOM_DA_ORDEM } from '@/api/rotulos'
import { useSessao } from '@/acesso/sessao'
import { useContaEncerrada } from '@/app/contaEncerrada'
import { Dados } from '@/componentes/Dados'
import { Aviso, Cabecalho, CarregandoTela, Erro } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { formatarCentavos } from '@/dinheiro/dinheiro'
import { AcoesDaVenda } from './AcoesDaVenda'
import { useCobrarDeNovo, useVenda } from './consultas'
import { nomeDoProduto, rotuloDaVenda, tomDaVenda } from './situacaoDaVenda'

/**
 * A venda aberta: o que se vendeu e a quem, o pagamento, os itens que saíram
 * na mesma cobrança, as ordens de serviço e o razão (só o dono). Corrigir,
 * receber, trocar a placa e cancelar são as mesmas ações da lista.
 */
export function TelaDaVenda() {
  const { id = '' } = useParams()
  const dono = useSessao((s) => s.perfil?.papel === 'Dono')
  const encerrada = useContaEncerrada()
  const consulta = useVenda(id)
  const cobrar = useCobrarDeNovo()
  const base = dono ? '/painel/vendas' : '/vendedor/vendas'
  const d = consulta.data

  if (consulta.isPending) return <CarregandoTela />
  if (!d) return <Erro erro={consulta.error} />

  const v = d.venda
  const podeCobrar = !encerrada && v.situacao === 'Registrada' && !v.cortesia && !v.doSistemaAnterior && (v.pagamento === null || v.pagamento === 'Falhou' || v.pagamento === 'Cancelada')

  return (
    <div className="mx-auto grid max-w-3xl gap-4">
      <Link to={base} className="flex w-fit items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
        <ChevronLeft aria-hidden className="size-4" />Vendas
      </Link>
      <Cabecalho titulo={d.cliente.nome} detalhe={nomeDoProduto(v)}>
        <Badge variant={tomDaVenda(v)}>{rotuloDaVenda(v)}</Badge>
      </Cabecalho>

      <Erro erro={cobrar.error ? mensagemDoErro(cobrar.error) : null} />
      {cobrar.data?.semCobranca && <Aviso>{cobrar.data.semCobranca}</Aviso>}
      {(podeCobrar || v.situacao === 'Registrada') && (
        <div className="flex flex-wrap gap-2">
          {podeCobrar && <Button disabled={cobrar.isPending} onClick={() => cobrar.mutate(v.id)}>Gerar cobrança</Button>}
          <AcoesDaVenda venda={v} dono={dono} naPagina />
        </div>
      )}

      <Card>
        <CardHeader><CardTitle>Venda</CardTitle></CardHeader>
        <CardContent>
          <Dados itens={[
            ['Produto', nomeDoProduto(v)],
            ['Placa', v.codigo ? <Link className="tabular-nums underline underline-offset-4" to={`/placa/${v.codigo}`}>{v.codigo}</Link> : '-'],
            ['Valor', formatarCentavos(v.valorCents)],
            ...(dono ? [['Vendedor', v.vendedor] as [string, string]] : []),
            ['Comissão', `${formatarCentavos(v.comissaoCents)}${d.comissaoRepassada ? ', repassada' : ''}`],
            ['Vendida em', formatarDataHora(v.vendidaEm)],
            ...(d.venceEm ? [['Vence em', formatarData(d.venceEm)] as [string, string]] : []),
            ...(v.situacao === 'Cancelada'
              ? [
                  ['Cancelada em', formatarDataHora(d.canceladaEm)] as [string, string],
                  ['Motivo', v.motivoDoCancelamento ? ROTULO_DO_CANCELAMENTO[v.motivoDoCancelamento] : '-'] as [string, string],
                  ...(d.observacaoDoCancelamento ? [['Observação', d.observacaoDoCancelamento] as [string, string]] : []),
                ]
              : []),
          ]} />
        </CardContent>
      </Card>

      <Card>
        <CardHeader><CardTitle>Cliente</CardTitle></CardHeader>
        <CardContent className="grid gap-3">
          <Dados itens={[
            ['Responsável', d.cliente.responsavel],
            ['Telefone', formatarTelefone(d.cliente.telefone)],
            ['CNPJ ou CPF', d.cliente.documento ?? '-'],
            ['E-mail', d.cliente.email ?? '-'],
          ]} />
          {dono && <Button asChild variant="outline" className="justify-self-start"><Link to="/painel/clientes">Abrir em Clientes</Link></Button>}
        </CardContent>
      </Card>

      <Card>
        <CardHeader><CardTitle>Pagamento</CardTitle></CardHeader>
        <CardContent>
          {d.cobranca
            ? (
                <Dados itens={[
                  ['Situação', d.cobranca.situacao ? ROTULO_DO_PAGAMENTO[d.cobranca.situacao] : '-'],
                  ['Meio', ROTULO_DO_MEIO[d.cobranca.meio]],
                  [d.itens.length > 0 ? 'Cobrança da venda inteira' : 'Cobrança', formatarCentavos(d.cobranca.valorCents)],
                  ...(d.cobranca.estornadoCents > 0 ? [['Estornado', formatarCentavos(d.cobranca.estornadoCents)] as [string, string]] : []),
                  ['Gerada em', formatarDataHora(d.cobranca.criadaEm)],
                  ...(d.cobranca.pagaEm ? [['Paga em', formatarDataHora(d.cobranca.pagaEm)] as [string, string]] : []),
                  ...(d.cobranca.situacao === 'Pendente' && d.cobranca.pixExpiraEm ? [['Pix vence em', formatarDataHora(d.cobranca.pixExpiraEm)] as [string, string]] : []),
                ]} />
              )
            : <p className="text-sm text-muted-foreground">{v.cortesia ? 'Cortesia, sem cobrança.' : v.doSistemaAnterior ? 'Venda do sistema anterior.' : 'Sem cobrança.'}</p>}
        </CardContent>
      </Card>

      {d.itens.length > 0 && (
        <Card>
          <CardHeader><CardTitle>Na mesma cobrança</CardTitle></CardHeader>
          <CardContent>
            <ul className="divide-y text-sm">
              {d.itens.map((i) => (
                <li key={i.id} className="flex items-baseline justify-between gap-4 py-2 first:pt-0 last:pb-0">
                  <Link className="min-w-0 underline-offset-4 hover:underline" to={`${base}/${i.id}`}>
                    {i.quantidade > 1 ? `${i.quantidade}x ${i.produto}` : i.produto}{i.codigo ? `, placa ${i.codigo}` : ''}
                    {i.situacao === 'Cancelada' && <span className="text-muted-foreground"> (cancelada)</span>}
                  </Link>
                  <span className="shrink-0 font-medium tabular-nums">{formatarCentavos(i.valorCents)}</span>
                </li>
              ))}
            </ul>
          </CardContent>
        </Card>
      )}

      {d.ordens.length > 0 && (
        <Card>
          <CardHeader><CardTitle>Ordem de serviço</CardTitle></CardHeader>
          <CardContent>
            <ul className="divide-y text-sm">
              {d.ordens.map((o) => (
                <li key={o.numero} className="flex items-center justify-between gap-4 py-2 first:pt-0 last:pb-0">
                  <span className="min-w-0">
                    <span className="tabular-nums">Nº {o.numero}</span>, {o.quantidade > 1 ? `${o.quantidade}x ` : ''}{o.produto}
                    <span className="block text-xs text-muted-foreground">Aberta em {formatarData(o.criadaEm)}{o.entregueEm ? `, entregue em ${formatarData(o.entregueEm)}` : ''}</span>
                  </span>
                  <Badge variant={TOM_DA_ORDEM[o.situacao]}>{ROTULO_DA_ORDEM[o.situacao]}</Badge>
                </li>
              ))}
            </ul>
          </CardContent>
        </Card>
      )}

      {dono && d.razao.length > 0 && (
        <Card>
          <CardHeader><CardTitle>Razão</CardTitle></CardHeader>
          <CardContent>
            <ul className="divide-y text-sm">
              {d.razao.map((l, i) => (
                <li key={i} className="flex items-baseline justify-between gap-4 py-2 first:pt-0 last:pb-0">
                  <span className="min-w-0">
                    {ROTULO_DO_LANCAMENTO[l.tipo]} {l.estorno && <Badge variant="destructive">Estorno</Badge>}
                    <span className="block text-xs text-muted-foreground">{formatarData(l.ocorridoEm)}, {l.descricao}</span>
                  </span>
                  <span className="shrink-0 font-medium tabular-nums">{formatarCentavos(l.valorCents)}</span>
                </li>
              ))}
            </ul>
          </CardContent>
        </Card>
      )}
    </div>
  )
}
