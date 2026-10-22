import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { mensagemDoErro } from '@/api/cliente'
import { formatarData, formatarDataHora, formatarTelefone, ROTULO_DO_PAGAMENTO } from '@/api/rotulos'
import type { DestinoInformado } from '@/api/tipos'
import { Aviso, Cabecalho, Erro, EtiquetaDoEstado } from '@/componentes/Tela'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { formatarCentavos } from '@/dinheiro/dinheiro'
import { PixNaTela } from '@/componentes/PixNaTela'
import { useAlternar, useApontar, useFicha, useRenovar } from './consultas'
import { destinoPronto } from './destino'
import { Historico } from '@/auditoria/Historico'
import { EscolhaDeDestino } from './EscolhaDeDestino'
import { GravarNfc } from './BotoesDeNfc'
import { Dados } from '@/componentes/Dados'

/** A ficha da placa: destino, vencimento, leituras do mês e o liga e desliga. */
export function TelaDaPlaca() {
  const { codigo = '' } = useParams()
  const ficha = useFicha(codigo)
  const apontar = useApontar(codigo)
  const alternar = useAlternar(codigo)
  const renovar = useRenovar(codigo)
  const [trocando, setTrocando] = useState(false)
  const [destino, setDestino] = useState<DestinoInformado>({ tipo: 'Google', lugarNoGoogle: null, link: null })
  const f = ficha.data
  const maior = Math.max(1, ...(f?.leituras.map((l) => l.total) ?? [0]))

  if (ficha.isPending) return <p role="status" className="text-sm text-muted-foreground">Carregando...</p>
  if (!f) return <Erro erro={ficha.error} />

  return (
    <div className="mx-auto grid max-w-3xl gap-4">
      <Cabecalho titulo={`Placa ${f.codigo}`}>
        <EtiquetaDoEstado estado={f.estado} />
      </Cabecalho>
      {f.estado === 'EmEstoque' && (
        <Card>
          <CardContent className="grid gap-3 pt-6">
            <p className="text-sm">No estoque{f.consignadaA ? `, com ${f.consignadaA}` : ''}.</p>
            <Button asChild><Link to={`/vender?codigo=${f.codigo}`}>Vender esta placa</Link></Button>
            <GravarNfc endereco={f.endereco} />
          </CardContent>
        </Card>
      )}
      {f.venda && (
        <Card>
          <CardHeader><CardTitle>Venda</CardTitle></CardHeader>
          <CardContent>
            <Dados itens={[
              ['Cliente', f.venda.cliente],
              ['Responsável', f.venda.responsavel],
              ['Telefone', formatarTelefone(f.venda.telefone)],
              ['Produto', f.venda.produto],
              ['Valor', formatarCentavos(f.venda.valorCents)],
              ['Pagamento', f.venda.pagamento ? ROTULO_DO_PAGAMENTO[f.venda.pagamento] : 'Sem cobrança'],
              ['Vendedor', f.venda.vendedor],
              ['Comissão', formatarCentavos(f.venda.comissaoCents)],
              ['Vendida em', formatarData(f.venda.vendidaEm)],
              ['Vence em', formatarData(f.venceEm)],
              ['Carência até', formatarData(f.fimDaCarencia)],
            ]} />
          </CardContent>
        </Card>
      )}
      {f.venda && (
        <Card>
          <CardHeader><CardTitle>Destino</CardTitle></CardHeader>
          <CardContent className="grid gap-3">
            <p className="text-sm break-all"><a className="underline underline-offset-4" href={f.destino ?? undefined} target="_blank" rel="noreferrer noopener">{f.destino}</a></p>
            <Erro erro={apontar.error || alternar.error ? mensagemDoErro(apontar.error ?? alternar.error) : null} />
            {trocando
              ? (
                  <>
                    <EscolhaDeDestino valor={destino} aoMudar={setDestino} nomeSugerido={f.venda.cliente} />
                    <div className="flex gap-2">
                      <Button disabled={!destinoPronto(destino) || apontar.isPending} onClick={() => apontar.mutate(destino, { onSuccess: () => setTrocando(false) })}>Salvar destino</Button>
                      <Button variant="ghost" onClick={() => setTrocando(false)}>Cancelar</Button>
                    </div>
                  </>
                )
              : (
                  <div className="grid grid-cols-2 gap-2 [&>*:last-child:nth-child(odd)]:col-span-2">
                    <Button variant="outline" onClick={() => setTrocando(true)}>Trocar destino</Button>
                    {(f.estado === 'Ativa' || f.estado === 'Carencia' || f.estado === 'Suspensa' || f.estado === 'Vencida') && (
                      <Button variant="outline" disabled={alternar.isPending} onClick={() => alternar.mutate()}>{f.estado === 'Suspensa' ? 'Pôr no ar' : 'Suspender'}</Button>
                    )}
                    <GravarNfc endereco={f.endereco} />
                  </div>
                )}
          </CardContent>
        </Card>
      )}
      {f.venda && (f.estado === 'Ativa' || f.estado === 'Carencia' || f.estado === 'Vencida') && (
        <Card>
          <CardHeader><CardTitle>Renovação</CardTitle></CardHeader>
          <CardContent className="grid gap-3">
            <Erro erro={renovar.error ? mensagemDoErro(renovar.error) : null} />
            {renovar.data?.semCobranca && <Aviso>{renovar.data.semCobranca}</Aviso>}
            {renovar.data?.cobranca?.situacao === 'Paga' && <p role="status" className="font-medium">Renovação paga. Vence em {formatarData(f.venceEm)}.</p>}
            {renovar.data?.cobranca?.pixCopiaECola && renovar.data.cobranca.situacao === 'Pendente' && (
              <>
                <p className="text-sm">Pix de {formatarCentavos(renovar.data.valorCents)}:</p>
                <PixNaTela copiaECola={renovar.data.cobranca.pixCopiaECola} imagemDoGateway={renovar.data.cobranca.pixQrCodeUrl} />
              </>
            )}
            {!renovar.data && (
              <div className="flex flex-wrap gap-2">
                <Button disabled={renovar.isPending} onClick={() => renovar.mutate(undefined)}>Renovar com Pix</Button>
                <Button variant="outline" disabled={renovar.isPending} onClick={() => renovar.mutate('Dinheiro')}>Recebido em dinheiro</Button>
                <Button variant="outline" disabled={renovar.isPending} onClick={() => renovar.mutate('OutroPix')}>Recebido por outro Pix</Button>
              </div>
            )}
          </CardContent>
        </Card>
      )}
      <Card>
        <CardHeader><CardTitle>Leituras</CardTitle></CardHeader>
        <CardContent className="grid gap-3">
          <p className="text-sm">{f.totalLeituras} leituras no total. Última: {formatarDataHora(f.ultimaLeituraEm)}.</p>
          {f.leituras.length > 0 && (
            <div className="flex h-24 items-end gap-0.5" role="img" aria-label={`Leituras dos últimos 30 dias: ${f.leituras.reduce((s, l) => s + l.total, 0)}`}>
              {f.leituras.map((l) => (
                <div key={l.dia} title={`${formatarData(l.dia)}: ${l.total}`} className="flex-1 rounded-t bg-primary" style={{ height: `${(l.total / maior) * 100}%` }} />
              ))}
            </div>
          )}
        </CardContent>
      </Card>
      <Card>
        <CardHeader><CardTitle>Histórico</CardTitle></CardHeader>
        <CardContent><Historico registros={f.historico} /></CardContent>
      </Card>
    </div>
  )
}
