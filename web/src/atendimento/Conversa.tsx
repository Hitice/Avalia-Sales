import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef, useState } from 'react'
import { chamarApi, enviarJson, mensagemDoErro } from '@/api/cliente'
import { formatarDataHora } from '@/api/rotulos'
import { Carregando, Erro } from '@/componentes/Tela'
import { Button } from '@/components/ui/button'
import { Textarea } from '@/components/ui/textarea'
import { cn } from '@/lib/utils'

export type MensagemDoChat = { id: string; daOperadora: boolean; autor: string; texto: string; enviadaEm: string; lidaEm: string | null }

/**
 * A conversa entre o revendedor e a Avalia. `caminho` é /chat para o dono e
 * /operadora/chat/{id} para a operadora. O aviso ao vivo "chat" refaz a lista.
 */
export function Conversa({ caminho, souOperadora }: { caminho: string; souOperadora: boolean }) {
  const consultas = useQueryClient()
  const mensagens = useQuery({ queryKey: ['chat', caminho], queryFn: () => chamarApi<MensagemDoChat[]>(caminho) })
  const [texto, setTexto] = useState('')
  const enviar = useMutation({
    mutationFn: (t: string) => chamarApi<MensagemDoChat>(caminho, enviarJson('POST', { texto: t })),
    onSuccess: () => {
      setTexto('')
      return consultas.invalidateQueries({ queryKey: ['chat'] })
    },
  })
  const fim = useRef<HTMLDivElement>(null)
  // Corpo em bloco: no Chromium atual o scrollIntoView devolve uma Promise, e efeito não pode devolver nada além da limpeza.
  useEffect(() => {
    fim.current?.scrollIntoView({ block: 'end' })
  }, [mensagens.data?.length])

  return (
    <div className="grid gap-3">
      <div className="grid max-h-96 gap-2 overflow-y-auto rounded-lg border bg-card p-3" role="log" aria-label="Mensagens">
        {mensagens.isPending && <Carregando linhas={2} />}
        {mensagens.data?.length === 0 && <p className="text-sm text-muted-foreground">Digite sua mensagem.</p>}
        {mensagens.data?.map((m) => {
          const minha = m.daOperadora === souOperadora
          return (
            <div key={m.id} className={cn('max-w-[85%] rounded-lg px-3 py-2 text-sm', minha ? 'justify-self-end bg-primary text-primary-foreground' : 'justify-self-start bg-muted')}>
              <p className="whitespace-pre-wrap">{m.texto}</p>
              <p className={cn('mt-1 text-xs', minha ? 'text-primary-foreground/80' : 'text-muted-foreground')}>{m.autor}, {formatarDataHora(m.enviadaEm)}{minha && m.lidaEm ? ', lida' : ''}</p>
            </div>
          )
        })}
        <div ref={fim} />
      </div>
      <Erro erro={enviar.error ? mensagemDoErro(enviar.error) : null} />
      <form
        className="grid gap-2"
        onSubmit={(e) => {
          e.preventDefault()
          if (texto.trim()) enviar.mutate(texto)
        }}
      >
        <Textarea aria-label="Mensagem" rows={3} maxLength={2000} value={texto} onChange={(e) => setTexto(e.target.value)} placeholder="Escreva a mensagem" />
        <Button type="submit" className="justify-self-end" disabled={!texto.trim() || enviar.isPending}>Enviar</Button>
      </form>
    </div>
  )
}
