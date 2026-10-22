import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Bell } from 'lucide-react'
import { useNavigate } from 'react-router'
import { chamarApi, enviarJson } from '@/api/cliente'
import { formatarDataHora } from '@/api/rotulos'
import type { CaixaDeNotificacoes } from '@/api/tipos'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuLabel, DropdownMenuSeparator, DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { cn } from '@/lib/utils'

/** O sino: as notificações do dono ou da operadora, com as não lidas no número. O aviso ao vivo "notificacao" refaz. */
export function Sino() {
  const consultas = useQueryClient()
  const navegar = useNavigate()
  const caixa = useQuery({ queryKey: ['notificacoes'], queryFn: () => chamarApi<CaixaDeNotificacoes>('/notificacoes'), refetchInterval: 120_000 })
  const ler = useMutation({
    mutationFn: (id: string | null) => chamarApi(id ? `/notificacoes/${id}/lida` : '/notificacoes/lidas', enviarJson('POST', {})),
    onSuccess: () => consultas.invalidateQueries({ queryKey: ['notificacoes'] }),
  })
  const naoLidas = caixa.data?.naoLidas ?? 0

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" size="icon" className="relative">
          <Bell aria-hidden className="size-5" />
          <span className="sr-only">Notificações{naoLidas > 0 ? `, ${naoLidas} não lidas` : ''}</span>
          {naoLidas > 0 && (
            <span aria-hidden className="absolute -top-0.5 -right-0.5 flex min-w-5 items-center justify-center rounded-full bg-destructive px-1 text-xs font-semibold text-primary-foreground tabular-nums">
              {naoLidas > 99 ? '99+' : naoLidas}
            </span>
          )}
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-80 max-w-[calc(100vw-2rem)]">
        <DropdownMenuLabel className="flex items-center justify-between">
          Notificações
          {naoLidas > 0 && (
            <button type="button" className="text-xs font-normal text-muted-foreground underline underline-offset-4" onClick={() => ler.mutate(null)}>
              Marcar todas como lidas
            </button>
          )}
        </DropdownMenuLabel>
        <DropdownMenuSeparator />
        <div className="max-h-96 overflow-y-auto">
          {caixa.data?.notificacoes.length === 0 && <p className="px-2 py-6 text-center text-sm text-muted-foreground">Nada por aqui.</p>}
          {caixa.data?.notificacoes.map((n) => (
            <DropdownMenuItem
              key={n.id}
              className="grid items-start gap-0.5 py-2"
              onSelect={() => {
                if (!n.lida) ler.mutate(n.id)
                if (n.link) navegar(n.link)
              }}
            >
              <span className="flex items-center gap-2">
                {!n.lida && <span aria-label="Não lida" className="size-2 shrink-0 rounded-full bg-primary" />}
                <span className={cn('text-sm', !n.lida && 'font-semibold')}>{n.titulo}</span>
              </span>
              <span className="text-xs text-muted-foreground">{n.texto}</span>
              <span className="text-xs text-muted-foreground">{n.revendedor ? `${n.revendedor}, ` : ''}{formatarDataHora(n.criadaEm)}</span>
            </DropdownMenuItem>
          ))}
        </div>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
