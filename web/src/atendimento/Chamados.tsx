import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef, useState } from 'react'
import { chamarApi, enviarJson, mensagemDoErro } from '@/api/cliente'
import { formatarDataHora } from '@/api/rotulos'
import type { Chamado, ChamadoComMensagens, MensagemDoChamado, SituacaoDoChamado } from '@/api/tipos'
import { Carregando, Erro } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Textarea } from '@/components/ui/textarea'
import { cn } from '@/lib/utils'

/** Rotas do dono (/chamados) ou da operadora (/operadora/chamados): a mesma tela serve os dois lados. */
export type BaseDosChamados = '/chamados' | '/operadora/chamados'

export const HORARIO_DE_ATENDIMENTO = 'Atendimento de segunda a sexta, das 8h às 18h. Resposta em até 24 horas úteis.'

const ROTULO_DA_SITUACAO: Record<SituacaoDoChamado, string> = {
  AguardandoAvalia: 'Aguardando a Avalia',
  AguardandoRevenda: 'Aguardando a revenda',
  Resolvido: 'Fechado',
}

export const useChamados = (base: BaseDosChamados) =>
  useQuery({ queryKey: ['chat', base], queryFn: () => chamarApi<Chamado[]>(base) })

/** Quanto falta para a Avalia responder, ou há quanto tempo passou. */
function prazo(c: Chamado, agora: number): { texto: string; atrasado: boolean } | null {
  if (!c.prazoEm) return null
  const horas = Math.round((Date.parse(c.prazoEm) - agora) / 3_600_000)
  if (c.atrasado) return { texto: horas <= -1 ? `Atrasado há ${-horas} h` : 'Atrasado', atrasado: true }
  return { texto: horas >= 1 ? `Responder em ${horas} h` : 'Responder em menos de 1 h', atrasado: false }
}

export function SituacaoDoChamadoEtiqueta({ chamado, operadora }: { chamado: Chamado; operadora: boolean }) {
  // O relógio fica fixo na renderização: a lista volta da API a cada aviso ao vivo.
  const [agora] = useState(() => Date.now())
  const p = prazo(chamado, agora)
  if (operadora && p) return <Badge variant={p.atrasado ? 'destructive' : 'secondary'}>{p.texto}</Badge>
  return <Badge variant={chamado.situacao === 'Resolvido' ? 'outline' : 'secondary'}>{ROTULO_DA_SITUACAO[chamado.situacao]}</Badge>
}

/** A lista de chamados: a operadora vê a revenda e o prazo; o dono, a situação. */
export function ListaDeChamados({ chamados, aberto, aoAbrir, operadora }: {
  chamados: Chamado[]
  aberto: string | null
  aoAbrir: (id: string) => void
  operadora: boolean
}) {
  return (
    <ul className="grid gap-1" aria-label="Chamados">
      {chamados.map((c) => (
        <li key={c.id}>
          <button
            type="button"
            aria-pressed={aberto === c.id}
            onClick={() => aoAbrir(c.id)}
            className={cn('grid min-h-11 w-full gap-1 rounded-md p-2 text-left text-sm', aberto === c.id ? 'bg-accent text-accent-foreground' : 'hover:bg-muted')}
          >
            <span className="flex items-start justify-between gap-2">
              <span className="min-w-0 font-medium">{operadora ? c.revendedor : c.assunto}</span>
              {c.naoLidas > 0 && <Badge>{c.naoLidas}</Badge>}
            </span>
            {operadora && <span className="truncate text-muted-foreground">{c.assunto}</span>}
            <span className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
              <SituacaoDoChamadoEtiqueta chamado={c} operadora={operadora} />
              Chamado {c.numero}, {formatarDataHora(c.ultimaMensagemEm)}
            </span>
          </button>
        </li>
      ))}
    </ul>
  )
}

/** As mensagens de um chamado, a resposta e o resolver. O aviso ao vivo "chat" refaz a conversa. */
export function ConversaDoChamado({ base, id, operadora }: { base: BaseDosChamados; id: string; operadora: boolean }) {
  const consultas = useQueryClient()
  const conversa = useQuery({ queryKey: ['chat', base, id], queryFn: () => chamarApi<ChamadoComMensagens>(`${base}/${id}`) })
  const [texto, setTexto] = useState('')
  const refazer = () => consultas.invalidateQueries({ queryKey: ['chat'] })
  const responder = useMutation({
    mutationFn: (t: string) => chamarApi<MensagemDoChamado>(`${base}/${id}/mensagens`, enviarJson('POST', { texto: t })),
    onSuccess: () => {
      setTexto('')
      return refazer()
    },
  })
  const resolver = useMutation({ mutationFn: () => chamarApi(`${base}/${id}/resolver`, enviarJson('POST', {})), onSuccess: refazer })
  const fim = useRef<HTMLDivElement>(null)
  // Corpo em bloco: no Chromium atual o scrollIntoView devolve uma Promise, e efeito não pode devolver nada além da limpeza.
  useEffect(() => {
    fim.current?.scrollIntoView({ block: 'end' })
  }, [conversa.data?.mensagens.length])

  if (conversa.isPending) return <Carregando linhas={2} />
  if (!conversa.data) return <Erro erro={conversa.error} />
  const { chamado, mensagens } = conversa.data

  return (
    <div className="grid gap-3">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="min-w-0">
          <h2 className="text-lg font-semibold">{chamado.assunto}</h2>
          <p className="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
            Chamado {chamado.numero}{operadora && `, ${chamado.revendedor}`}
            <SituacaoDoChamadoEtiqueta chamado={chamado} operadora={operadora} />
          </p>
        </div>
        {chamado.situacao !== 'Resolvido' && (
          <Button variant="outline" onClick={() => resolver.mutate()} disabled={resolver.isPending}>Fechar</Button>
        )}
      </div>
      <div className="grid max-h-112 gap-2 overflow-y-auto rounded-lg border bg-card p-3" role="log" aria-label="Mensagens">
        {mensagens.map((m) => {
          const minha = m.daOperadora === operadora
          return (
            <div key={m.id} className={cn('max-w-5/6 rounded-lg px-3 py-2 text-sm', minha ? 'justify-self-end bg-primary text-primary-foreground' : 'justify-self-start bg-muted')}>
              <p className="whitespace-pre-wrap">{m.texto}</p>
              <p className={cn('mt-1 text-xs', minha ? 'text-primary-foreground/80' : 'text-muted-foreground')}>{m.autor}, {formatarDataHora(m.enviadaEm)}{minha && m.lidaEm ? ', lida' : ''}</p>
            </div>
          )
        })}
        <div ref={fim} />
      </div>
      <Erro erro={responder.error ?? resolver.error ? mensagemDoErro(responder.error ?? resolver.error) : null} />
      <form
        className="grid gap-2"
        onSubmit={(e) => {
          e.preventDefault()
          if (texto.trim()) responder.mutate(texto)
        }}
      >
        <Textarea aria-label="Mensagem" rows={3} maxLength={2000} value={texto} onChange={(e) => setTexto(e.target.value)} placeholder="Digite sua mensagem" />
        <Button type="submit" className="justify-self-end" disabled={!texto.trim() || responder.isPending}>
          {chamado.situacao === 'Resolvido' && !operadora ? 'Reabrir e enviar' : 'Enviar'}
        </Button>
      </form>
    </div>
  )
}
