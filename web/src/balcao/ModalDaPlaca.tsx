import { useCallback, useState } from 'react'
import { Link } from 'react-router'
import { ROTULO_DO_ESTADO } from '@/api/rotulos'
import type { DestinoInformado, ItemDoCatalogo, Lugar } from '@/api/tipos'
import { Aviso, Erro } from '@/componentes/Tela'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Field, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { formatarCentavos } from '@/dinheiro/dinheiro'
import { type LinhaDaVenda, linhaDePlaca } from './itens'
import { extrairCodigo } from './codigo'
import { useFicha } from './consultas'
import { destinoPronto } from './destino'
import { EscolhaDeDestino } from './EscolhaDeDestino'
import { LeitorDeQr } from './LeitorDeQr'

export const DESTINO_VAZIO: DestinoInformado = { tipo: 'Google', lugarNoGoogle: null, link: null }

/** Folha de baixo no celular, centro no computador: a mesma das duas folhas da Nova venda. */
export const FOLHA_DA_VENDA = 'top-auto bottom-0 left-0 altura-da-folha max-w-full translate-x-0 translate-y-0 overflow-y-auto rounded-b-none area-segura-baixo-4 max-sm:border-x-0 max-sm:border-b-0 max-sm:data-open:slide-in-from-bottom-8 max-sm:data-closed:slide-out-to-bottom-8 sm:top-1/2 sm:bottom-auto sm:left-1/2 sm:altura-da-folha sm:max-w-md sm:-translate-x-1/2 sm:-translate-y-1/2 sm:rounded-xl sm:pb-4'

/**
 * A placa entra na venda por aqui: ler o QR (ou digitar o código), conferir
 * que está no estoque e dizer para onde ela leva. No celular abre como folha
 * de baixo; no computador, no centro. Confirmar devolve a linha pronta.
 */
export function ModalDaPlaca({ item, opcoes, codigoInicial = '', codigosNaVenda, aoFechar, aoConfirmar }: {
  item: ItemDoCatalogo
  /** Os produtos com placa do catálogo: com mais de um, dá para trocar aqui. */
  opcoes: ItemDoCatalogo[]
  codigoInicial?: string
  codigosNaVenda: string[]
  aoFechar: () => void
  aoConfirmar: (linha: LinhaDaVenda) => void
}) {
  const [produtoId, setProdutoId] = useState(item.id)
  const produto = opcoes.find((p) => p.id === produtoId) ?? item
  const [digitado, setDigitado] = useState(codigoInicial)
  const [codigo, setCodigo] = useState(() => extrairCodigo(codigoInicial))
  const [camera, setCamera] = useState(false)
  const [destino, setDestino] = useState<DestinoInformado>(DESTINO_VAZIO)
  const [lugar, setLugar] = useState<Lugar | null>(null)
  const ficha = useFicha(codigo || undefined)
  const emEstoque = ficha.data?.estado === 'EmEstoque'
  const repetida = !!codigo && codigosNaVenda.includes(codigo)
  const pronta = emEstoque && !repetida && destinoPronto(destino)
  const lido = useCallback((c: string) => {
    setCamera(false)
    setDigitado(c)
    setCodigo(c)
  }, [])

  return (
    <Dialog open onOpenChange={(aberto) => !aberto && aoFechar()}>
      <DialogContent className={FOLHA_DA_VENDA}>
        <DialogHeader>
          <DialogTitle>{produto.nome}</DialogTitle>
          <DialogDescription>{formatarCentavos(produto.precoCents)} por placa. Leia o QR e diga para onde ela leva.</DialogDescription>
        </DialogHeader>
        {opcoes.length > 1 && (
          <Field>
            <FieldLabel htmlFor="produto-da-placa">Produto</FieldLabel>
            <Select value={produtoId} onValueChange={setProdutoId}>
              <SelectTrigger id="produto-da-placa" className="w-full"><SelectValue /></SelectTrigger>
              <SelectContent>{opcoes.map((p) => <SelectItem key={p.id} value={p.id}>{p.nome}, {formatarCentavos(p.precoCents)}</SelectItem>)}</SelectContent>
            </Select>
          </Field>
        )}

        <section aria-labelledby="titulo-da-placa" className="grid gap-3">
          <h3 id="titulo-da-placa" className="font-medium">Placa</h3>
          {camera
            ? <LeitorDeQr aoLer={lido} aoFechar={() => setCamera(false)} />
            : (
                <>
                  <Button size="lg" onClick={() => setCamera(true)}>Ler QR</Button>
                  <form
                    className="flex gap-2"
                    onSubmit={(e) => {
                      e.preventDefault()
                      setCodigo(extrairCodigo(digitado))
                    }}
                  >
                    <Input aria-label="Código da placa" className="uppercase tabular-nums" placeholder="K7M2PX" autoComplete="off" value={digitado} onChange={(e) => setDigitado(e.target.value)} />
                    <Button type="submit" variant="outline">Conferir</Button>
                  </form>
                  {digitado && !extrairCodigo(digitado) && <p className="text-sm text-destructive">O código tem 6 caracteres.</p>}
                </>
              )}
          {codigo && ficha.isPending && <p role="status" className="text-sm text-muted-foreground">Conferindo {codigo}...</p>}
          <Erro erro={ficha.error} />
          {repetida && <Aviso>A placa {codigo} já está nesta venda.</Aviso>}
          {ficha.data && !emEstoque && (
            <Aviso>A placa {ficha.data.codigo} está {ROTULO_DO_ESTADO[ficha.data.estado].toLowerCase()}. <Link className="underline" to={`/placa/${ficha.data.codigo}`}>Abrir a ficha</Link></Aviso>
          )}
          {ficha.data && emEstoque && !repetida && (
            <p className="rounded-md border border-sucesso-borda bg-sucesso-fundo p-3 text-sm">Placa <span className="font-semibold tabular-nums">{ficha.data.codigo}</span> no estoque{ficha.data.consignadaA ? `, com ${ficha.data.consignadaA}` : ''}.</p>
          )}
        </section>

        <section aria-labelledby="titulo-do-destino" className="grid gap-3">
          <h3 id="titulo-do-destino" className="font-medium">Para onde a placa leva</h3>
          <EscolhaDeDestino valor={destino} aoMudar={setDestino} nomeSugerido="" lugar={lugar} aoEscolherLugar={setLugar} />
        </section>

        <Button size="lg" disabled={!pronta} onClick={() => aoConfirmar(linhaDePlaca(produto, codigo, destino, lugar))}>Adicionar à venda</Button>
      </DialogContent>
    </Dialog>
  )
}
