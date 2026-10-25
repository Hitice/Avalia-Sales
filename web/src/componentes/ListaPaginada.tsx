import type { ReactNode } from 'react'
import { BuscaDaLista, Paginacao } from '@/componentes/Paginacao'
import { Carregando, Erro, Vazio } from '@/componentes/Tela'
import type { EstadoDaLista } from '@/lib/paginacao'

/**
 * O miolo de toda lista paginada pela API: a busca (que vai à API), o estado
 * da consulta (carregando, erro, vazio), a lista que a tela desenha e o pé
 * com a faixa e Anterior/Próxima. Enquanto a página seguinte chega, a
 * anterior fica na tela, esmaecida. Quem usa passa a tabela com `<Table semBusca>`.
 */
export function ListaPaginada<T>({ lista, textoVazio, acaoVazia, nomeDaBusca = 'Buscar na lista', placeholder, children }: {
  lista: EstadoDaLista<T>
  textoVazio: string
  acaoVazia?: ReactNode
  nomeDaBusca?: string
  placeholder?: string
  children: (itens: T[]) => ReactNode
}) {
  const { consulta, itens, total, pagina, tamanho, termo } = lista
  const atualizando = consulta.isPlaceholderData || lista.esperandoBusca || (consulta.isFetching && !consulta.isPending)
  const buscando = termo.trim() !== ''
  const comBusca = total > 0 || buscando

  return (
    <div className="grid gap-3">
      {comBusca && <BuscaDaLista valor={termo} aoMudar={lista.mudarTermo} rotulo={nomeDaBusca} placeholder={placeholder} />}
      {consulta.isPending && <Carregando />}
      {consulta.error != null && !itens && <Erro erro={consulta.error} />}
      {itens && itens.length === 0 && (buscando
        ? <p className="py-1 text-sm text-muted-foreground" role="status">Nada na lista com "{termo.trim()}".</p>
        : <Vazio texto={textoVazio} acao={acaoVazia} />)}
      {itens && itens.length > 0 && (
        <div aria-busy={atualizando} className={atualizando ? 'opacity-60 transition-opacity duration-150' : 'transition-opacity duration-150'}>
          {children(itens)}
        </div>
      )}
      {itens && <Paginacao pagina={pagina} tamanho={tamanho} total={total} itensNaPagina={itens.length} aoMudar={lista.irParaPagina} atualizando={atualizando} />}
    </div>
  )
}
