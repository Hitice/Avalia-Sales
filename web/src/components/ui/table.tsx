"use client"

import * as React from "react"
import { cn } from "cn"
import { useRegiaoRolavel } from "@/lib/regiaoRolavel"
import { Input } from "./input"

/**
 * O que a coluna guarda, declarado só no cabeçalho (TableHead). No
 * computador (index.css): número à direita e data, selo e ações na largura do
 * conteúdo, para o texto ficar com o resto; selos da coluna com a largura do
 * maior; ações sempre no "⋯" (AcoesDaLinha), na última coluna.
 */
export type TipoDaColuna = "texto" | "numero" | "data" | "situacao" | "acoes"

/**
 * Copia o título e o tipo de cada coluna para as células (data-rotulo e
 * data-tipo). O CSS do celular mostra o rótulo ao lado do valor; a primeira
 * coluna é o título do cartão e a de ações ocupa a linha toda, sem rótulo.
 */
function marcarColunas(tabela: HTMLTableElement | null) {
  if (!tabela?.tHead) return
  const cabecalhos = Array.from(tabela.tHead.rows[0]?.cells ?? [])
  const titulos = cabecalhos.map((th, i) =>
    i === 0 || th.dataset.tipo === "acoes" || th.querySelector(".sr-only") ? null : th.textContent?.trim() || null)
  const tipos = cabecalhos.map((th) => th.dataset.tipo ?? null)
  for (const corpo of Array.from(tabela.tBodies)) {
    for (const linha of Array.from(corpo.rows)) {
      const alinhada = linha.cells.length === cabecalhos.length
      Array.from(linha.cells).forEach((celula, i) => {
        const rotulo = alinhada ? titulos[i] : null
        if (rotulo) celula.dataset.rotulo = rotulo
        else delete celula.dataset.rotulo
        const tipo = alinhada ? tipos[i] : null
        if (tipo) celula.dataset.tipo = tipo
        else delete celula.dataset.tipo
      })
    }
  }
}

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
 * No celular (abaixo de md) cada linha vira um cartão e a lista corre com a
 * página, sem rolagem própria. No computador, tabela do painel (DESIGN.md):
 * rola dentro dela com o cabeçalho fixo, cabeçalho em 12 px sobre o cinza-50,
 * linhas de 44 px com fio claro. Nos dois, a partir de sete linhas ganha a
 * busca: o texto filtra pelo que aparece na linha, sem acento. Tabela
 * paginada pela API não usa essa busca (ela só enxergaria a página aberta):
 * leva `semBusca`, e a tela põe o campo que manda o termo à API
 * (`ListaPaginada`).
 */
function Table({ className, semBusca = false, ...props }: React.ComponentProps<"table"> & { semBusca?: boolean }) {
  const ref = React.useRef<HTMLTableElement>(null)
  const area = useRegiaoRolavel<HTMLDivElement>("Lista")
  const [termo, setTermo] = React.useState("")
  const [contagem, setContagem] = React.useState({ total: 0, visiveis: 0 })
  const termoAtual = React.useRef(termo)
  termoAtual.current = termo

  const atualizar = React.useCallback(() => {
    const tabela = ref.current
    marcarColunas(tabela)
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

  const comBusca = !semBusca && (contagem.total >= LINHAS_PARA_BUSCA || termo.length > 0)
  return (
    <div data-slot="table-container" className="relative w-full">
      {comBusca && (
        <div className="mb-3 flex items-center gap-3 md:px-5 md:pt-1">
          <Input type="search" value={termo} onChange={(e) => setTermo(e.target.value)} placeholder="Buscar na lista" aria-label="Buscar na lista" className="md:max-w-xs" />
          {termo.trim() && <p className="shrink-0 text-xs text-muted-foreground" aria-live="polite">{contagem.visiveis} de {contagem.total}</p>}
        </div>
      )}
      <div ref={area} className="md:rolagem-da-tabela md:sombra-de-rolagem">
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

/** Título da coluna. A coluna de ações não precisa de texto: o leitor de tela ouve "Ações". */
function TableHead({ className, tipo = "texto", children, ...props }: React.ComponentProps<"th"> & { tipo?: TipoDaColuna }) {
  return (
    <th
      data-slot="table-head"
      data-tipo={tipo}
      className={cn(
        "h-9 px-3 text-left align-middle text-xs font-medium whitespace-nowrap text-muted-foreground md:first:pl-5 md:last:pr-5 [&:has([role=checkbox])]:pr-0",
        className
      )}
      {...props}
    >
      {tipo === "acoes" && !children ? <span className="sr-only">Ações</span> : children}
    </th>
  )
}

function TableCell({ className, ...props }: React.ComponentProps<"td">) {
  return (
    <td
      data-slot="table-cell"
      className={cn(
        "h-11 px-3 py-1 align-middle whitespace-nowrap md:first:pl-5 md:last:pr-5 [&:has([role=checkbox])]:pr-0",
        // Celular: a primeira célula é o título do cartão; com rótulo, uma linha de rótulo à esquerda e valor à direita;
        // sem rótulo e não primeira, as ações, numa fileira de botões de mesma largura. Texto longo quebra em qualquer
        // ponto em vez de passar da largura do cartão.
        "max-md:h-auto max-md:min-w-0 max-md:p-0 max-md:wrap-anywhere max-md:whitespace-normal",
        "max-md:first:pb-2 max-md:first:text-base max-md:first:font-semibold",
        // O rótulo flutua à esquerda e o valor, inteiro (mesmo com mais de um pedaço), fica à direita.
        "max-md:data-[rotulo]:flow-root max-md:data-[rotulo]:py-1.5 max-md:data-[rotulo]:text-right",
        "max-md:before:float-left max-md:before:pr-4 max-md:before:text-left max-md:before:text-sm max-md:before:font-normal max-md:before:text-muted-foreground max-md:rotulo-da-celula",
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
  TableBody,
  TableCaption,
  TableCell,
  TableFooter,
  TableHead,
  TableHeader,
  TableRow,
}
