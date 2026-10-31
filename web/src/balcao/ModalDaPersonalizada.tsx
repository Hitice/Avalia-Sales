import { useState } from 'react'
import type { DestinoInformado, ItemDoCatalogo, Lugar } from '@/api/tipos'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { formatarCentavos } from '@/dinheiro/dinheiro'
import { type LinhaDaVenda, linhaPersonalizada } from './itens'
import { destinoPronto } from './destino'
import { EscolhaDeDestino } from './EscolhaDeDestino'
import { DESTINO_VAZIO, FOLHA_DA_VENDA } from './ModalDaPlaca'

/**
 * A placa personalizada entra na venda por aqui: um destino por QR, sem ler código, porque a Avalia faz a placa depois
 * da venda paga. Um QR por vez na tela: o seletor de destino tem campos com id fixo. O primeiro é o da tag NFC.
 */
export function ModalDaPersonalizada({ item, aoFechar, aoConfirmar }: {
  item: ItemDoCatalogo
  aoFechar: () => void
  aoConfirmar: (linha: LinhaDaVenda) => void
}) {
  const quantos = Math.max(1, item.codigosNaPlaca ?? 1)
  const [destinos, setDestinos] = useState<DestinoInformado[]>(() => Array.from({ length: quantos }, () => DESTINO_VAZIO))
  const [lugares, setLugares] = useState<(Lugar | null)[]>(() => Array.from({ length: quantos }, () => null))
  const [atual, setAtual] = useState(0)
  const pronta = destinos.every(destinoPronto)
  const mudar = <T,>(lista: T[], valor: T) => lista.map((v, i) => (i === atual ? valor : v))

  return (
    <Dialog open onOpenChange={(aberto) => !aberto && aoFechar()}>
      <DialogContent className={FOLHA_DA_VENDA}>
        <DialogHeader>
          <DialogTitle>{item.nome}</DialogTitle>
          <DialogDescription>{formatarCentavos(item.precoCents)}. Diga para onde leva cada QR.</DialogDescription>
        </DialogHeader>
        {quantos > 1 && (
          <Tabs value={String(atual)} onValueChange={(v) => setAtual(Number(v))}>
            <TabsList className="w-full">
              {destinos.map((d, i) => (
                // A chave é a posição: a lista tem tamanho fixo, o do produto.
                // eslint-disable-next-line react/no-array-index-key
                <TabsTrigger key={i} value={String(i)}>QR {i + 1}{destinoPronto(d) ? ' ✓' : ''}</TabsTrigger>
              ))}
            </TabsList>
          </Tabs>
        )}
        <section aria-label={`Destino do QR ${atual + 1}`} className="grid gap-3">
          <EscolhaDeDestino key={atual} valor={destinos[atual]} aoMudar={(d) => setDestinos((l) => mudar(l, d))} nomeSugerido=""
            lugar={lugares[atual]} aoEscolherLugar={(l) => setLugares((todos) => mudar(todos, l))} />
        </section>
        <Button size="lg" disabled={!pronta} onClick={() => aoConfirmar(linhaPersonalizada(item, destinos))}>Adicionar à venda</Button>
      </DialogContent>
    </Dialog>
  )
}
