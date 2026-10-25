import { LoaderCircle } from 'lucide-react'
import type { ReactNode } from 'react'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { mensagemDoErro } from '@/api/cliente'
import { ROTULO_DO_ESTADO, TOM_DO_ESTADO } from '@/api/rotulos'
import type { EstadoDaPlaca } from '@/api/tipos'

/**
 * Título da tela (20 px) com um detalhe curto ao lado, como a data, e as
 * ações à direita. No celular as ações quebram para a linha de baixo.
 */
export function Cabecalho({ titulo, detalhe, children }: { titulo: string; detalhe?: ReactNode; children?: ReactNode }) {
  return (
    <div className="mb-4 flex min-h-9 flex-wrap items-center justify-between gap-x-4 gap-y-3 md:mb-5">
      <div className="flex min-w-0 flex-wrap items-baseline gap-x-3 gap-y-1">
        <h1 className="text-xl font-medium">{titulo}</h1>
        {detalhe && <p className="text-sm text-muted-foreground">{detalhe}</p>}
      </div>
      {children && <div className="flex flex-wrap gap-2">{children}</div>}
    </div>
  )
}

/** Linhas pulsando no lugar da lista que ainda vem da API: só onde a espera é real. */
export function Carregando({ linhas = 3 }: { linhas?: number }) {
  return (
    <div role="status" className="grid gap-2">
      {Array.from({ length: linhas }, (_, i) => <div key={i} aria-hidden className="h-11 animate-pulse rounded-md bg-muted motion-reduce:animate-none" />)}
      <span className="sr-only">Carregando...</span>
    </div>
  )
}

/** A tela inteira esperando: sessão, código da tela ou dado sem o qual nada aparece. */
export function CarregandoTela() {
  return (
    <div role="status" className="flex min-h-48 items-center justify-center gap-2 p-6 text-sm text-muted-foreground">
      <LoaderCircle aria-hidden className="size-5 animate-spin motion-reduce:animate-none" />
      Carregando...
    </div>
  )
}

/** Lista vazia: uma linha que diz o que falta e, quando houver, a ação que resolve. */
export function Vazio({ texto, acao }: { texto: string; acao?: ReactNode }) {
  return (
    <div className="flex flex-col items-start gap-3 py-2 sm:flex-row sm:items-center sm:justify-between">
      <p className="text-sm text-muted-foreground">{texto}</p>
      {acao}
    </div>
  )
}

/** Carregando, erro ou vazio: o estado da consulta antes da lista. */
export function EstadoDaConsulta({ carregando, erro, vazio, textoVazio, acao }: { carregando: boolean; erro: unknown; vazio: boolean; textoVazio: string; acao?: ReactNode }) {
  if (carregando) return <Carregando />
  if (erro) return <Erro erro={erro} />
  if (vazio) return <Vazio texto={textoVazio} acao={acao} />
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

/** Situação da placa. Na coluna de situação da tabela, o selo estica até o mais largo dela (index.css). */
export function EtiquetaDoEstado({ estado }: { estado: EstadoDaPlaca }) {
  return <Badge variant={TOM_DO_ESTADO[estado]}>{ROTULO_DO_ESTADO[estado]}</Badge>
}

/**
 * A tabela dentro do cartão. No celular, as linhas viram cartões na largura
 * toda e correm com a página, sem barra de rolagem por cima do texto; no
 * computador, a tabela encosta nas bordas do cartão (index.css).
 */
export function Rolagem({ children }: { children: ReactNode }) {
  return <div data-slot="rolagem" className="-mx-4 px-4 md:-mx-5 md:px-0">{children}</div>
}
