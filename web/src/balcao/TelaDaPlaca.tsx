import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { mensagemDoErro } from '@/api/cliente'
import { useSessao } from '@/acesso/sessao'
import { formatarData, formatarDataHora, formatarTelefone, ROTULO_DO_MEIO, ROTULO_DO_PAGAMENTO } from '@/api/rotulos'
import type { DestinoInformado } from '@/api/tipos'
import { Aviso, Cabecalho, CarregandoTela, Erro, EtiquetaDoEstado } from '@/componentes/Tela'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { formatarCentavos } from '@/dinheiro/dinheiro'
import { PixNaTela } from '@/componentes/PixNaTela'
import { useAlternar, useApontar, useDesfazerRenovacao, useFicha, useRenovar } from './consultas'
import { destinoPronto } from './destino'
import { Historico } from '@/auditoria/Historico'
import { EscolhaDeDestino } from './EscolhaDeDestino'
import { GravarNfc } from './BotoesDeNfc'
import { Dados } from '@/componentes/Dados'
import { porDiaDe, VendasEm30Dias } from '@/painel/VendasEm30Dias'

/** A ficha da placa: destino, vencimento, leituras do mês e o liga e desliga. */
export function TelaDaPlaca() {
  const { codigo = '' } = useParams()
  const ficha = useFicha(codigo)
  const apontar = useApontar(codigo)
  const alternar = useAlternar(codigo)
  const renovar = useRenovar(codigo)
  const desfazer = useDesfazerRenovacao(codigo)
  const dono = useSessao((s) => s.perfil?.papel === 'Dono')
  // Recebimento por fora estende a placa e lança a receita: só depois de confirmar.
  const [porFora, setPorFora] = useState<'Dinheiro' | 'OutroPix' | null>(null)
  const [desfazendo, setDesfazendo] = useState(false)
  const [trocando, setTrocando] = useState(false)
  const [destino, setDestino] = useState<DestinoInformado>({ tipo: 'Google', lugarNoGoogle: null, link: null })
  const f = ficha.data
  const [agora] = useState(() => Date.now())

  if (ficha.isPending) return <CarregandoTela />
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
      {/* A renovação é anual e só do revendedor. */}
      {dono && f.venda && (f.estado === 'Ativa' || f.estado === 'Carencia' || f.estado === 'Vencida') && (
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
                <Button variant="outline" disabled={renovar.isPending} onClick={() => setPorFora('Dinheiro')}>Recebido em dinheiro</Button>
                <Button variant="outline" disabled={renovar.isPending} onClick={() => setPorFora('OutroPix')}>Recebido por outro Pix</Button>
              </div>
            )}
            {f.ultimaRenovacao && (
              <div className="flex flex-wrap items-center justify-between gap-2 border-t pt-3 text-sm">
                <p>Última renovação: {formatarCentavos(f.ultimaRenovacao.valorCents)}, {ROTULO_DO_MEIO[f.ultimaRenovacao.meio].toLowerCase()}, em {formatarData(f.ultimaRenovacao.pagaEm)}.</p>
                {dono && f.ultimaRenovacao.porFora && <Button variant="ghost" className="text-destructive" onClick={() => { desfazer.reset(); setDesfazendo(true) }}>Desfazer</Button>}
              </div>
            )}
          </CardContent>
        </Card>
      )}
      <Dialog open={porFora !== null} onOpenChange={(aberto) => !aberto && setPorFora(null)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Renovação recebida {porFora === 'Dinheiro' ? 'em dinheiro' : 'por outro Pix'}</DialogTitle>
            <DialogDescription>A placa ganha mais 12 meses e o valor entra no razão.</DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="ghost" onClick={() => setPorFora(null)}>Cancelar</Button>
            <Button disabled={renovar.isPending} onClick={() => { if (porFora) renovar.mutate(porFora); setPorFora(null) }}>Confirmar</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
      <Dialog open={desfazendo} onOpenChange={setDesfazendo}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Desfazer a renovação</DialogTitle>
            <DialogDescription>A placa volta ao vencimento de antes e o valor sai do razão por estorno.</DialogDescription>
          </DialogHeader>
          <Erro erro={desfazer.error ? mensagemDoErro(desfazer.error) : null} />
          <DialogFooter>
            <Button variant="ghost" onClick={() => setDesfazendo(false)}>Cancelar</Button>
            <Button variant="destructive" disabled={desfazer.isPending} onClick={() => desfazer.mutate(undefined, { onSuccess: () => setDesfazendo(false) })}>Desfazer</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
      <VendasEm30Dias titulo="Leituras nos últimos 30 dias" serie="Leituras" carregando={false} porDia={porDiaDe(f.leituras, agora)}
        detalhe={<p className="mb-3 text-sm">{f.totalLeituras} leituras no total. Última: {formatarDataHora(f.ultimaLeituraEm)}.</p>} />
      <Card>
        <CardHeader><CardTitle>Histórico</CardTitle></CardHeader>
        <CardContent><Historico registros={f.historico} /></CardContent>
      </Card>
    </div>
  )
}
