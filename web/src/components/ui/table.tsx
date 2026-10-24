"use client"

import * as React from "react"
import { cn } from "cn"
import { Input } from "./input"

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
 * colunas a 390 px. No computador, tabela do painel (DESIGN.md): cabeçalho em
 * 12 px sobre o cinza-50, linhas de 44 px com fio claro, e a primeira e a
 * última coluna alinhadas ao texto do cartão.
 */
/** Sem acento e em minúsculas: "Ótica" acha "otica". */
const normalizar = (texto: string) => texto.normalize("NFD").replace(/\p{Diacritic}/gu, "").toLowerCase()

/** Esconde as linhas que não têm o termo e devolve quantas há e quantas ficaram. */
function filtrar(tabela: HTMLTableElement | null, termo: string) {
  const linhas = Array.from(tabela?.tBodies ?? []).flatMap((corpo) => Array.from(corpo.rows))
  const busca = normalizar(termo.trim())
  let visiveis = 0
  for (const linha of linhas) {
    const esconder = busca.length > 0 && !normalizar(linha.textContent ?? "").includes(busca)
    if (esconder !== linha.hidden) linha.hidden = esconder
    if (!esconder) visiveis++
  }
  return { total: linhas.length, visiveis }
}

/** A partir de quantas linhas a tabela ganha o campo de busca. */
const LINHAS_PARA_BUSCA = 7

/**
 * Toda tabela rola dentro dela com o cabeçalho fixo e, a partir de sete
 * linhas, ganha a busca: o texto filtra pelo que aparece na linha, sem
 * acento. Tabela paginada pela API busca só na página aberta; a tela dela tem
 * o filtro que vai à API.
 */
function Table({ className, ...props }: React.ComponentProps<"table">) {
  const ref = React.useRef<HTMLTableElement>(null)
  const [termo, setTermo] = React.useState("")
  const [contagem, setContagem] = React.useState({ total: 0, visiveis: 0 })
  const termoAtual = React.useRef(termo)
  termoAtual.current = termo

  const atualizar = React.useCallback(() => {
    const tabela = ref.current
    rotular(tabela)
    const nova = filtrar(tabela, termoAtual.current)
    setContagem((velha) => (velha.total === nova.total && velha.visiveis === nova.visiveis ? velha : nova))
  }, [])
  React.useLayoutEffect(atualizar)
  React.useEffect(() => {
    const tabela = ref.current
    if (!tabela) return
    const observador = new MutationObserver(atualizar)
    observador.observe(tabela, { childList: true, subtree: true, characterData: true })
    return () => observador.disconnect()
  }, [atualizar])

  const comBusca = contagem.total >= LINHAS_PARA_BUSCA || termo.length > 0
  return (
    <div data-slot="table-container" className="relative w-full">
      {comBusca && (
        <div className="mb-3 flex items-center gap-3 md:px-5 md:pt-1">
          <Input type="search" value={termo} onChange={(e) => setTermo(e.target.value)} placeholder="Buscar na lista" aria-label="Buscar na lista" className="md:max-w-xs" />
          {termo.trim() && <p className="shrink-0 text-xs text-muted-foreground" aria-live="polite">{contagem.visiveis} de {contagem.total}</p>}
        </div>
      )}
      {/* Área que rola recebe foco: quem usa teclado também rola a lista. */}
      <div className="rolagem-da-tabela md:sombra-de-rolagem md:overflow-x-auto" tabIndex={0} role="region" aria-label="Lista">
        <table
          ref={ref}
          data-slot="table"
          className={cn("w-full caption-bottom text-sm max-md:block", className)}
          {...props}
        />
      </div>
      {comBusca && termo.trim() && contagem.visiveis === 0 && <p className="py-3 text-sm text-muted-foreground md:px-5">Nada na lista com "{termo.trim()}".</p>}
    </div>
  )
}

function TableHeader({ className, ...props }: React.ComponentProps<"thead">) {
  return (
    <thead
      data-slot="table-header"
      className={cn("[&_tr]:border-b md:bg-cinza-50 md:[&_tr]:hover:bg-transparent max-md:sr-only", className)}
      {...props}
    />
  )
}

function TableBody({ className, ...props }: React.ComponentProps<"tbody">) {
  return (
    <tbody
      data-slot="table-body"
      className={cn("[&_tr:last-child]:border-0 md:[&_tr]:border-cinza-100 max-md:block", className)}
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
        "border-b transition-colors duration-150 hover:bg-cinza-50 has-aria-expanded:bg-cinza-50 data-[state=selected]:bg-muted max-md:flex max-md:flex-col max-md:py-4 max-md:hover:bg-transparent",
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
        "h-9 px-3 text-left align-middle text-xs font-medium whitespace-nowrap text-muted-foreground md:first:pl-5 md:last:pr-5 [&:has([role=checkbox])]:pr-0",
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
        "h-11 px-3 py-1 align-middle whitespace-nowrap md:first:pl-5 md:last:pr-5 [&:has([role=checkbox])]:pr-0",
        // Celular: a primeira célula é o título do cartão; com rótulo, uma linha de rótulo à esquerda e valor à direita;
        // sem rótulo e não primeira, as ações, numa fileira de botões de mesma largura.
        "max-md:h-auto max-md:min-w-0 max-md:p-0 max-md:whitespace-normal",
        "max-md:first:pb-2 max-md:first:text-base max-md:first:font-semibold",
        // O rótulo flutua à esquerda e o valor, inteiro (mesmo com mais de um pedaço), fica à direita.
        "max-md:data-[rotulo]:flow-root max-md:data-[rotulo]:py-1.5 max-md:data-[rotulo]:text-right",
        "max-md:before:float-left max-md:before:pr-4 max-md:before:text-left max-md:before:text-sm max-md:before:font-normal max-md:before:text-muted-foreground max-md:rotulo-da-celula",
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
