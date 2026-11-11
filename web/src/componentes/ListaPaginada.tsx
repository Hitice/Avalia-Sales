import { Download } from 'lucide-react'
import { type ReactNode, useState } from 'react'
import { mensagemDoErro } from '@/api/cliente'
import { BuscaDaLista, Paginacao } from '@/componentes/Paginacao'
import { Carregando, Erro, Vazio } from '@/componentes/Tela'
import { Button } from '@/components/ui/button'
import { baixarCsv, type Exportacao, paraCsv } from '@/lib/exportacao'
import type { EstadoDaLista } from '@/lib/paginacao'

/**
 * O miolo de toda lista paginada pela API: a busca (que vai à API), o estado
 * da consulta (carregando, erro, vazio), a lista que a tela desenha e o pé
 * com a faixa e Anterior/Próxima. Enquanto a página seguinte chega, a
 * anterior fica na tela, esmaecida. Quem usa passa a tabela com `<Table semBusca>`.
 * Os filtros da tabela (`Filtro`) vão em `filtros`, ao lado da busca, e
 * `filtrada` diz se algum está ligado: a lista vazia então é do filtro, não da tela.
 * Com `exportar`, o botão Exportar grava em CSV todas as páginas com a busca e os
 * filtros de agora, nas colunas que a tela declara.
 */
export function ListaPaginada<T>({ lista, textoVazio, acaoVazia, nomeDaBusca = 'Buscar na lista', placeholder, filtros, filtrada = false, exportar, children }: {
  lista: EstadoDaLista<T>
  textoVazio: string
  acaoVazia?: ReactNode
  nomeDaBusca?: string
  placeholder?: string
  filtros?: ReactNode
  filtrada?: boolean
  exportar?: Exportacao<T>
  children: (itens: T[]) => ReactNode
}) {
  const [exportando, setExportando] = useState(false)
  const [erroDaExportacao, setErroDaExportacao] = useState<unknown>(null)
  const { consulta, itens, total, pagina, tamanho, termo } = lista
  const atualizando = consulta.isPlaceholderData || lista.esperandoBusca || (consulta.isFetching && !consulta.isPending)
  const buscando = termo.trim() !== ''
  const comBusca = total > 0 || buscando || filtrada

  async function exportarTudo() {
    if (!exportar || !lista.baixarTodos) return
    setExportando(true)
    setErroDaExportacao(null)
    try {
      baixarCsv(exportar.arquivo, paraCsv(await lista.baixarTodos(), exportar.colunas))
    } catch (e) {
      setErroDaExportacao(e)
    } finally {
      setExportando(false)
    }
  }

  return (
    <div className="grid gap-3">
      {comBusca && (
        <div className="flex flex-wrap gap-2">
          <BuscaDaLista valor={termo} aoMudar={lista.mudarTermo} rotulo={nomeDaBusca} placeholder={placeholder} />
          {filtros}
          {exportar && lista.baixarTodos && (
            <Button variant="outline" className="sm:ml-auto" disabled={exportando || total === 0} onClick={() => void exportarTudo()}>
              <Download aria-hidden />{exportando ? 'Exportando...' : 'Exportar'}
            </Button>
          )}
        </div>
      )}
      <Erro erro={erroDaExportacao ? mensagemDoErro(erroDaExportacao) : null} />
      {consulta.isPending && <Carregando />}
      {consulta.error != null && !itens && <Erro erro={consulta.error} />}
      {itens && itens.length === 0 && (buscando
        ? <p className="py-1 text-sm text-muted-foreground" role="status">Nada na lista com "{termo.trim()}"{filtrada ? ' e esses filtros' : ''}.</p>
        : filtrada
          ? <p className="py-1 text-sm text-muted-foreground" role="status">Nada na lista com esses filtros.</p>
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
