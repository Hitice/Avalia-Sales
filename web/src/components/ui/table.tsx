"use client"

import * as React from "react"
import { cn } from "cn"

/**
 * Copia o título de cada coluna para as células (data-rotulo), que o CSS do
 * celular mostra em cima do valor. A primeira coluna é o título do cartão e a
 * coluna sem título visível (ações) ocupa a linha toda: não levam rótulo.
 */
function rotular(tabela: HTMLTableElement | null) {
  if (!tabela?.tHead) return
  const titulos = Array.from(tabela.tHead.rows[0]?.cells ?? []).map((th, i) =>
    i === 0 || th.querySelector(".sr-only") ? null : th.textContent?.trim() || null)
  for (const corpo of Array.from(tabela.tBodies)) {
    for (const linha of Array.from(corpo.rows)) {
      Array.from(linha.cells).forEach((celula, i) => {
        const rotulo = linha.cells.length === titulos.length ? titulos[i] : null
        if (rotulo) celula.dataset.rotulo = rotulo
        else delete celula.dataset.rotulo
      })
    }
  }
}

/**
 * No celular (abaixo de md) cada linha vira um cartão: a tabela larga cortava
 * colunas a 390 px. No computador, tabela comum.
 */
function Table({ className, ...props }: React.ComponentProps<"table">) {
  const ref = React.useRef<HTMLTableElement>(null)
  React.useLayoutEffect(() => rotular(ref.current))
  React.useEffect(() => {
    const tabela = ref.current
    if (!tabela) return
    const observador = new MutationObserver(() => rotular(tabela))
    observador.observe(tabela, { childList: true, subtree: true })
    return () => observador.disconnect()
  }, [])

  return (
    <div
      data-slot="table-container"
      className="relative w-full md:sombra-de-rolagem md:overflow-x-auto"
    >
      <table
        ref={ref}
        data-slot="table"
        className={cn("w-full caption-bottom text-sm max-md:block", className)}
        {...props}
      />
    </div>
  )
}

function TableHeader({ className, ...props }: React.ComponentProps<"thead">) {
  return (
    <thead
      data-slot="table-header"
      className={cn("[&_tr]:border-b max-md:sr-only", className)}
      {...props}
    />
  )
}

function TableBody({ className, ...props }: React.ComponentProps<"tbody">) {
  return (
    <tbody
      data-slot="table-body"
      className={cn("[&_tr:last-child]:border-0 max-md:block", className)}
      {...props}
    />
  )
}

function TableFooter({ className, ...props }: React.ComponentProps<"tfoot">) {
  return (
    <tfoot
      data-slot="table-footer"
      className={cn(
        "border-t bg-muted/50 font-medium [&>tr]:last:border-b-0",
        className
      )}
      {...props}
    />
  )
}

function TableRow({ className, ...props }: React.ComponentProps<"tr">) {
  return (
    <tr
      data-slot="table-row"
      className={cn(
        "border-b transition-colors hover:bg-muted/50 has-aria-expanded:bg-muted/50 data-[state=selected]:bg-muted max-md:flex max-md:flex-col max-md:py-4 max-md:hover:bg-transparent",
        className
      )}
      {...props}
    />
  )
}

function TableHead({ className, ...props }: React.ComponentProps<"th">) {
  return (
    <th
      data-slot="table-head"
      className={cn(
        "h-10 px-2 text-left align-middle font-medium whitespace-nowrap text-foreground [&:has([role=checkbox])]:pr-0",
        className
      )}
      {...props}
    />
  )
}

function TableCell({ className, ...props }: React.ComponentProps<"td">) {
  return (
    <td
      data-slot="table-cell"
      className={cn(
        "p-2 align-middle whitespace-nowrap [&:has([role=checkbox])]:pr-0",
        // Celular: a primeira célula é o título do cartão; com rótulo, uma linha de rótulo à esquerda e valor à direita;
        // sem rótulo e não primeira, as ações, numa fileira de botões de mesma largura.
        "max-md:min-w-0 max-md:p-0 max-md:whitespace-normal",
        "max-md:first:pb-2 max-md:first:text-base max-md:first:font-semibold",
        // O rótulo flutua à esquerda e o valor, inteiro (mesmo com mais de um pedaço), fica à direita.
        "max-md:data-[rotulo]:flow-root max-md:data-[rotulo]:py-1.5 max-md:data-[rotulo]:text-right",
        "max-md:before:float-left max-md:before:pr-4 max-md:before:text-left max-md:before:text-sm max-md:before:font-normal max-md:before:text-muted-foreground max-md:before:content-[attr(data-rotulo)]",
        // Computador: botões lado a lado quebram para a linha de baixo antes de a tabela passar da largura do cartão.
        "md:[&>div.flex]:flex-wrap",
        "max-md:[&:not([data-rotulo]):not(:first-child)]:pt-3 max-md:[&:not([data-rotulo]):not(:first-child)>div]:grid max-md:[&:not([data-rotulo]):not(:first-child)>div]:auto-cols-fr max-md:[&:not([data-rotulo]):not(:first-child)>div]:grid-flow-col",
        className
      )}
      {...props}
    />
  )
}

function TableCaption({
  className,
  ...props
}: React.ComponentProps<"caption">) {
  return (
    <caption
      data-slot="table-caption"
      className={cn("mt-4 text-sm text-muted-foreground", className)}
      {...props}
    />
  )
}

export {
  Table,
  TableHeader,
  TableBody,
  TableFooter,
  TableHead,
  TableRow,
  TableCell,
  TableCaption,
}
