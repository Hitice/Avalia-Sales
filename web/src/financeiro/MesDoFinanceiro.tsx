import { useState } from 'react'
import type { UseMutationResult } from '@tanstack/react-query'
import { mensagemDoErro } from '@/api/cliente'
import { formatarData } from '@/api/rotulos'
import { Erro } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Field, FieldLabel } from '@/components/ui/field'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Textarea } from '@/components/ui/textarea'
import { mesCorrente, nomeDoMes } from './fatias'

type Mes = { competencia: string; fechado: boolean }

/** O mês do financeiro no cabeçalho: os meses da API, ou só o aberto enquanto a lista não chega. */
export function SeletorDoMes({ meses, atual, valor, aoTrocar }: { meses: Mes[] | undefined; atual: Mes | undefined; valor: string | undefined; aoTrocar: (mes: string) => void }) {
  return (
    <Select value={valor ?? ''} onValueChange={aoTrocar}>
      <SelectTrigger aria-label="Mês" className="w-48"><SelectValue placeholder="Mês" /></SelectTrigger>
      <SelectContent>
        {(meses ?? (atual ? [atual] : [])).map((m) => (
          <SelectItem key={m.competencia} value={m.competencia}>{nomeDoMes(m.competencia)}{m.fechado ? ' (fechado)' : ''}</SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}

/** "Fechado em 03/11/2026", abaixo do título, quando o mês escolhido está fechado. */
export function MesFechado({ fechadoEm }: { fechadoEm: string | null | undefined }) {
  return <p className="mb-3 text-sm text-muted-foreground"><Badge variant="neutro">Fechado</Badge> em {formatarData(fechadoEm ?? null)}</p>
}

/**
 * Fechar o mês, com confirmação; reabrir pede o motivo, que vai à auditoria. Vale para o
 * financeiro da revenda e para o livro da operadora, cada um com as suas gravações.
 */
export function Fechamento({ competencia, fechado, fechar, reabrir }: {
  competencia: string
  fechado: boolean
  fechar: UseMutationResult<unknown, Error, string>
  reabrir: UseMutationResult<unknown, Error, { competencia: string; motivo: string }>
}) {
  const [aberto, setAberto] = useState(false)
  const [motivo, setMotivo] = useState('')
  const gravacao = fechado ? reabrir : fechar
  // Só mês que já acabou se fecha.
  if (!fechado && competencia >= mesCorrente()) return null

  async function confirmar() {
    try {
      if (fechado) await reabrir.mutateAsync({ competencia, motivo })
      else await fechar.mutateAsync(competencia)
      setAberto(false)
      setMotivo('')
    } catch {
      // O erro aparece no diálogo.
    }
  }

  return (
    <>
      <Button variant="outline" onClick={() => { gravacao.reset(); setAberto(true) }}>{fechado ? 'Reabrir mês' : 'Fechar mês'}</Button>
      <Dialog open={aberto} onOpenChange={setAberto}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{fechado ? 'Reabrir' : 'Fechar'} {nomeDoMes(competencia)}</DialogTitle>
            <DialogDescription>{fechado ? 'Lançamentos voltam a entrar no mês.' : 'Correções depois vão para o mês corrente.'}</DialogDescription>
          </DialogHeader>
          <Erro erro={gravacao.error ? mensagemDoErro(gravacao.error) : null} />
          {fechado && (
            <Field>
              <FieldLabel htmlFor="motivo-da-reabertura">Motivo</FieldLabel>
              <Textarea id="motivo-da-reabertura" maxLength={200} value={motivo} onChange={(e) => setMotivo(e.target.value)} />
            </Field>
          )}
          <DialogFooter>
            <Button variant="ghost" onClick={() => setAberto(false)}>Cancelar</Button>
            <Button disabled={gravacao.isPending || (fechado && !motivo.trim())} onClick={confirmar}>{fechado ? 'Reabrir mês' : 'Fechar mês'}</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  )
}
