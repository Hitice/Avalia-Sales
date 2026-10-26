import { useState } from 'react'
import { mensagemDoErro } from '@/api/cliente'
import { useSessao } from '@/acesso/sessao'
import { useLimiteDeDestinos, useSolicitarLiberacaoDeDestinos } from '@/painel/consultasDoLimite'
import { Aviso, Erro } from '@/componentes/Tela'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Field, FieldLabel } from '@/components/ui/field'
import { Textarea } from '@/components/ui/textarea'

/** O contador aparece quando restam 3 cadastros ou menos. */
const RESTANTES_PARA_AVISAR = 3

/**
 * O limite diário de cadastros de destino (placa vendida com destino, destino
 * trocado, link curto). Some enquanto sobra folga; perto do fim mostra a
 * contagem; esgotado, o dono pede a liberação e o vendedor pede ao dono.
 */
export function AvisoDoLimiteDeDestinos() {
  const dono = useSessao((s) => s.perfil?.papel === 'Dono')
  const limite = useLimiteDeDestinos()
  const [aberto, setAberto] = useState(false)
  const [motivo, setMotivo] = useState('')
  const pedir = useSolicitarLiberacaoDeDestinos()
  const situacao = limite.data
  if (!situacao || situacao.limite - situacao.usados > RESTANTES_PARA_AVISAR) return null

  return (
    <Aviso>
      <span className="grid gap-2">
        <span>Hoje: {situacao.usados} de {situacao.limite} cadastros de destino</span>
        {situacao.esgotado && (dono
          ? (
              <span className="flex flex-wrap items-center gap-2">
                <span>Limite do dia atingido.</span>
                {pedir.isSuccess
                  ? <span role="status" className="font-medium">Pedido enviado</span>
                  : <Button size="sm" variant="outline" onClick={() => { pedir.reset(); setAberto(true) }}>Solicitar liberação</Button>}
              </span>
            )
          : <span>Limite do dia atingido. Peça ao revendedor para solicitar a liberação.</span>)}
      </span>
      {dono && (
        <Dialog open={aberto} onOpenChange={setAberto}>
          <DialogContent>
            <DialogHeader><DialogTitle>Solicitar liberação</DialogTitle></DialogHeader>
            <Erro erro={pedir.error ? mensagemDoErro(pedir.error) : null} />
            <Field>
              <FieldLabel htmlFor="motivo-do-limite">Motivo (opcional)</FieldLabel>
              <Textarea id="motivo-do-limite" rows={3} maxLength={1000} value={motivo} onChange={(e) => setMotivo(e.target.value)} />
            </Field>
            <DialogFooter>
              <Button variant="outline" onClick={() => setAberto(false)}>Voltar</Button>
              <Button disabled={pedir.isPending} onClick={() => pedir.mutate(motivo, { onSuccess: () => { setMotivo(''); setAberto(false) } })}>Confirmar</Button>
            </DialogFooter>
          </DialogContent>
        </Dialog>
      )}
    </Aviso>
  )
}
