import { useQuery } from '@tanstack/react-query'
import { chamarApi } from '@/api/cliente'
import type { Chamado, ResumoDaFila } from '@/api/tipos'
import { useListaPaginada } from '@/lib/paginacao'
import type { BaseDosChamados } from './Chamados'

/** Os chamados em páginas; o aviso ao vivo "chat" refaz as que estão guardadas. */
export const useChamados = (base: BaseDosChamados) => useListaPaginada<Chamado>(['chat', base, 'lista'], base)

/** Quantos esperam a Avalia e quantos passaram do prazo, na fila inteira (a lista só traz uma página). */
export const useResumoDaFila = () =>
  useQuery({ queryKey: ['chat', 'resumo'], queryFn: () => chamarApi<ResumoDaFila>('/operadora/chamados/resumo') })
