import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { chamarApi } from '@/api/cliente'
import { formatarDataHora } from '@/api/rotulos'
import { Cabecalho, EstadoDaConsulta } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent } from '@/components/ui/card'
import { cn } from '@/lib/utils'
import { Conversa } from './Conversa'

type ConversaResumo = { revendedorId: string; revendedor: string; ultimaMensagem: string; ultimaEm: string; naoLidas: number }

/** A operadora atende os revendedores: as conversas com mensagem não lida vêm primeiro. */
export function TelaDeAtendimento() {
  const conversas = useQuery({ queryKey: ['chat', 'conversas'], queryFn: () => chamarApi<ConversaResumo[]>('/operadora/chat') })
  const [aberta, setAberta] = useState<ConversaResumo | null>(null)

  return (
    <>
      <Cabecalho titulo="Atendimento" />
      <div className="grid gap-4 lg:grid-cols-[20rem_1fr]">
        <Card className="self-start">
          <CardContent className="grid gap-1 p-2">
            <EstadoDaConsulta carregando={conversas.isPending} erro={conversas.error} vazio={conversas.data?.length === 0} textoVazio="Nenhuma conversa ainda." />
            {conversas.data?.map((c) => (
              <button
                key={c.revendedorId}
                type="button"
                aria-pressed={aberta?.revendedorId === c.revendedorId}
                onClick={() => setAberta(c)}
                className={cn('grid min-h-11 gap-0.5 rounded-md p-2 text-left text-sm', aberta?.revendedorId === c.revendedorId ? 'bg-accent' : 'hover:bg-muted')}
              >
                <span className="flex items-center justify-between gap-2 font-medium">
                  {c.revendedor}
                  {c.naoLidas > 0 && <Badge>{c.naoLidas}</Badge>}
                </span>
                <span className="truncate text-muted-foreground">{c.ultimaMensagem}</span>
                <span className="text-xs text-muted-foreground">{formatarDataHora(c.ultimaEm)}</span>
              </button>
            ))}
          </CardContent>
        </Card>
        {aberta
          ? <Conversa key={aberta.revendedorId} caminho={`/operadora/chat/${aberta.revendedorId}`} souOperadora />
          : <p className="text-sm text-muted-foreground">Escolha uma conversa.</p>}
      </div>
    </>
  )
}
