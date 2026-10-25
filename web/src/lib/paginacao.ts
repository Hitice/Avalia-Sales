import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { chamarApi } from '@/api/cliente'
import type { Pagina } from '@/api/tipos'

/** O tamanho que a tela pede; a API devolve no máximo 100, peça o que pedir. */
export const TAMANHO_DA_PAGINA = 50

/** Espera o usuário parar de digitar para a busca ir à API. */
export function useAtrasado<T>(valor: T, milissegundos = 300): T {
  const [atrasado, setAtrasado] = useState(valor)
  useEffect(() => {
    const espera = setTimeout(() => setAtrasado(valor), milissegundos)
    return () => clearTimeout(espera)
  }, [valor, milissegundos])
  return atrasado
}

type Filtros = Record<string, string | number | boolean | null | undefined>

/** O que a lista na tela precisa de uma lista paginada; `useListaPaginada` devolve isto e mais. */
export type EstadoDaLista<T> = {
  consulta: { isPending: boolean; isFetching: boolean; isPlaceholderData: boolean; error: unknown }
  itens: T[] | undefined
  total: number
  pagina: number
  tamanho: number
  termo: string
  esperandoBusca: boolean
  mudarTermo: (termo: string) => void
  irParaPagina: (pagina: number) => void
}

/** A query string sem o que está vazio, na ordem das chaves. */
export function paraQuery(parametros: Filtros): string {
  const busca = new URLSearchParams()
  for (const [chave, valor] of Object.entries(parametros)) {
    if (valor !== undefined && valor !== null && valor !== '') busca.set(chave, String(valor))
  }
  return busca.toString()
}

/**
 * Lista paginada pela API: página, busca e filtros vão na chave da consulta,
 * então cada combinação tem a sua cópia e uma resposta que chega atrasada não
 * pisa na que está na tela. A página anterior fica visível enquanto a próxima
 * chega. Mudar a busca ou um filtro volta à primeira página; a busca espera o
 * usuário parar de digitar. Se uma gravação esvazia a última página, volta
 * para a última que ainda tem item.
 *
 * Gravação que invalida a raiz da chave (`invalidateQueries({ queryKey: [raiz] })`)
 * refaz todas as páginas guardadas.
 */
export function useListaPaginada<T, R = Pagina<T>>(
  chave: readonly unknown[],
  caminho: string,
  { filtros = {}, tamanho = TAMANHO_DA_PAGINA, habilitada = true, extrair = (resposta) => resposta as unknown as Pagina<T> }: {
    filtros?: Filtros
    tamanho?: number
    habilitada?: boolean
    /** Quando a lista vem dentro de outra resposta (o razão traz os totais do mês junto). */
    extrair?: (resposta: R) => Pagina<T>
  } = {},
) {
  const [termo, setTermoBruto] = useState('')
  const [pagina, setPagina] = useState(1)
  const busca = useAtrasado(termo.trim())
  const filtrosEmTexto = paraQuery(filtros)
  const [filtrosVistos, setFiltrosVistos] = useState(filtrosEmTexto)
  if (filtrosVistos !== filtrosEmTexto) {
    // Mudou um filtro da tela: a lista recomeça na primeira página.
    setFiltrosVistos(filtrosEmTexto)
    setPagina(1)
  }

  const consulta = useQuery({
    queryKey: [...chave, { pagina, tamanho, busca, filtros: filtrosEmTexto }],
    queryFn: ({ signal }) => chamarApi<R>(`${caminho}?${paraQuery({ ...filtros, busca, pagina, tamanho })}`, { signal }),
    placeholderData: keepPreviousData,
    enabled: habilitada,
  })

  const bruto = consulta.data
  const dados = bruto ? extrair(bruto) : undefined
  if (dados && !consulta.isPlaceholderData && dados.itens.length === 0 && dados.total > 0 && pagina > 1) {
    // A gravação esvaziou a última página: volta para a última que ainda tem item.
    setPagina(Math.max(1, Math.ceil(dados.total / tamanho)))
  }

  return {
    consulta,
    /** A resposta inteira, para quem recebe mais que a lista. */
    bruto,
    dados,
    itens: dados?.itens,
    total: dados?.total ?? 0,
    pagina,
    tamanho,
    irParaPagina: setPagina,
    termo,
    mudarTermo: (novo: string) => {
      setTermoBruto(novo)
      setPagina(1)
    },
    /** A busca digitada ainda não foi à API: a lista na tela é a da busca anterior. */
    esperandoBusca: termo.trim() !== busca,
  }
}
