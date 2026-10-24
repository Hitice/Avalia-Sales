import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Bell, TriangleAlert } from 'lucide-react'
import { useNavigate } from 'react-router'
import { chamarApi, enviarJson } from '@/api/cliente'
import { formatarDataHora } from '@/api/rotulos'
import type { CaixaDeNotificacoes, MinhaRevenda, SituacaoDoContrato } from '@/api/tipos'
import { useSessao } from '@/acesso/sessao'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuLabel, DropdownMenuSeparator, DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { cn } from '@/lib/utils'

const CONTRATO: Partial<Record<SituacaoDoContrato, string>> = {
  Rascunho: 'Preparando o seu contrato de revenda.',
  AguardandoAssinatura: 'Assine o contrato de revenda que chegou no seu e-mail.',
  Cancelado: 'O contrato de revenda foi cancelado. Fale com o suporte.',
}

type Pendencia = { texto: string; link: string | null }

/** O que falta na revenda para operar: fica no topo do sino até resolver. */
function usePendencias(): Pendencia[] {
  const dono = useSessao((s) => s.perfil?.papel === 'Dono')
  const revenda = useQuery({ queryKey: ['revenda'], queryFn: () => chamarApi<MinhaRevenda>('/revenda'), enabled: dono })
  const r = revenda.data?.revendedor
  if (!dono || !r) return []
  const lista: Pendencia[] = []
  if (r.encerradaEm) return [{ texto: 'Conta encerrada. Exporte os dados em Meus dados.', link: '/conta/dados' }]
  if (r.encerramentoPedidoEm) lista.push({ texto: 'Assine o distrato que chegou no seu e-mail.', link: '/conta/dados' })
  if (!r.contratoAssinadoEm) lista.push({ texto: CONTRATO[revenda.data?.contrato ?? 'Rascunho'] ?? CONTRATO.Rascunho!, link: null })
  if (!r.temEndereco) lista.push({ texto: 'Informe o endereço da empresa em Meus dados.', link: '/conta/dados' })
  if (!r.recebedorPagarme) lista.push({ texto: 'Conta de recebimento no Pagar.me pendente.', link: '/painel/suporte' })
  if (!r.celular) lista.push({ texto: 'Informe o celular da revenda em Meus dados.', link: '/conta/dados' })
  return lista
}

/** O sino: as pendências da revenda e as notificações do dono ou da operadora. O aviso ao vivo "notificacao" refaz. */
export function Sino() {
  const pendencias = usePendencias()
  const consultas = useQueryClient()
  const navegar = useNavigate()
  const caixa = useQuery({ queryKey: ['notificacoes'], queryFn: () => chamarApi<CaixaDeNotificacoes>('/notificacoes'), refetchInterval: 120_000 })
  const ler = useMutation({
    mutationFn: (id: string | null) => chamarApi(id ? `/notificacoes/${id}/lida` : '/notificacoes/lidas', enviarJson('POST', {})),
    onSuccess: () => consultas.invalidateQueries({ queryKey: ['notificacoes'] }),
  })
  const naoLidas = (caixa.data?.naoLidas ?? 0) + pendencias.length

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" size="icon" className="relative">
          <Bell aria-hidden className="size-4.5" />
          <span className="sr-only">Notificações{naoLidas > 0 ? `, ${naoLidas} não lidas` : ''}</span>
          {naoLidas > 0 && (
            <span aria-hidden className="absolute top-0.5 right-0.5 flex h-4 min-w-4 items-center justify-center rounded-full bg-primary px-1 text-2xs font-medium text-primary-foreground tabular-nums ring-2 ring-card md:top-0 md:right-0 [[data-na-cor]_&]:bg-card [[data-na-cor]_&]:text-primary-texto [[data-na-cor]_&]:ring-primary">
              {naoLidas > 99 ? '99+' : naoLidas}
            </span>
          )}
        </Button>
      </DropdownMenuTrigger>
      {/* No celular o sino fica perto da borda: a margem de colisão mantém o menu dentro da tela. */}
      <DropdownMenuContent align="end" collisionPadding={12} className="w-80 max-w-(--radix-dropdown-menu-content-available-width)">
        <DropdownMenuLabel className="flex items-center justify-between">
          Notificações
          {(caixa.data?.naoLidas ?? 0) > 0 && (
            <button type="button" className="text-xs font-normal text-muted-foreground underline underline-offset-4" onClick={() => ler.mutate(null)}>
              Marcar todas como lidas
            </button>
          )}
        </DropdownMenuLabel>
        <DropdownMenuSeparator />
        <div className="max-h-96 overflow-y-auto">
          {pendencias.map((p) => (
            <DropdownMenuItem key={p.texto} className="items-start gap-2 py-2" onSelect={() => p.link && navegar(p.link)}>
              <TriangleAlert aria-hidden className="mt-0.5 size-4 shrink-0 text-atencao" />
              <span className="text-sm">{p.texto}</span>
            </DropdownMenuItem>
          ))}
          {pendencias.length > 0 && <DropdownMenuSeparator />}
          {caixa.data?.notificacoes.length === 0 && pendencias.length === 0 && <p className="px-2 py-6 text-center text-sm text-muted-foreground">Nada por aqui.</p>}
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
                <span className={cn('text-sm', !n.lida && 'font-medium')}>{n.titulo}</span>
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
