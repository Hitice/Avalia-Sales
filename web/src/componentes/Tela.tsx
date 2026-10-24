import { LoaderCircle } from 'lucide-react'
import type { ReactNode } from 'react'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { mensagemDoErro } from '@/api/cliente'
import { ROTULO_DO_ESTADO } from '@/api/rotulos'
import type { EstadoDaPlaca } from '@/api/tipos'

/** Título da tela com a ação principal ao lado. */
export function Cabecalho({ titulo, children }: { titulo: string; children?: ReactNode }) {
  return (
    <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
      <h1 className="text-2xl font-semibold">
        {titulo}
      </h1>
      {children && <div className="flex flex-wrap gap-2">{children}</div>}
    </div>
  )
}

/** Blocos pulsando no lugar do conteúdo que ainda vem da API. */
export function Carregando({ linhas = 3 }: { linhas?: number }) {
  return (
    <div role="status" className="grid gap-3">
      {Array.from({ length: linhas }, (_, i) => <div key={i} aria-hidden className="h-14 animate-pulse rounded-lg bg-muted" />)}
      <span className="sr-only">Carregando...</span>
    </div>
  )
}

/** A tela inteira esperando: sessão, código da tela ou dado sem o qual nada aparece. */
export function CarregandoTela() {
  return (
    <div role="status" className="flex min-h-48 items-center justify-center gap-2 p-6 text-sm text-muted-foreground">
      <LoaderCircle aria-hidden className="size-5 animate-spin" />
      Carregando...
    </div>
  )
}

/** Carregando, erro ou vazio: o estado da consulta antes da lista. */
export function EstadoDaConsulta({ carregando, erro, vazio, textoVazio }: { carregando: boolean; erro: unknown; vazio: boolean; textoVazio: string }) {
  if (carregando) return <Carregando />
  if (erro) return <Erro erro={erro} />
  if (vazio) return <p className="text-sm text-muted-foreground">{textoVazio}</p>
  return null
}

export function Erro({ erro }: { erro: unknown }) {
  if (!erro) return null
  return (
    <Alert variant="destructive">
      <AlertDescription>{typeof erro === 'string' ? erro : mensagemDoErro(erro)}</AlertDescription>
    </Alert>
  )
}

export function Aviso({ children }: { children: ReactNode }) {
  return (
    <Alert>
      <AlertDescription>{children}</AlertDescription>
    </Alert>
  )
}

const VARIANTE_DO_ESTADO: Record<EstadoDaPlaca, 'default' | 'secondary' | 'destructive' | 'outline'> = {
  EmEstoque: 'outline',
  Ativa: 'default',
  Carencia: 'secondary',
  Vencida: 'destructive',
  Suspensa: 'secondary',
  Baixada: 'outline',
}

export function EtiquetaDoEstado({ estado }: { estado: EstadoDaPlaca }) {
  return <Badge variant={VARIANTE_DO_ESTADO[estado]}>{ROTULO_DO_ESTADO[estado]}</Badge>
}

/** Rolagem horizontal para tabela larga no celular, sem a página inteira rolar de lado. */
export function Rolagem({ children }: { children: ReactNode }) {
  return <div className="-mx-4 overflow-x-auto px-4">{children}</div>
}
