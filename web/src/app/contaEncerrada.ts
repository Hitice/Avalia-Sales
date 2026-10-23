import { useQuery } from '@tanstack/react-query'
import { chamarApi } from '@/api/cliente'
import type { MinhaRevenda } from '@/api/tipos'
import { useSessao } from '@/acesso/sessao'

/** Telas que só servem para vender ou mexer na conta: somem da revenda encerrada pelo distrato. */
const SO_COM_A_CONTA_ATIVA = ['/vender', '/links', '/catalogo', '/painel/marca', '/painel/mapa']

export const soComAContaAtiva = (caminho: string) => SO_COM_A_CONTA_ATIVA.some((t) => caminho === t || caminho.startsWith(`${t}/`))

/** A revenda do dono logado, se encerrada pelo distrato (a API recusa a escrita; a tela esconde o que só serve para vender). */
export function useContaEncerrada() {
  const dono = useSessao((s) => s.perfil?.papel === 'Dono')
  const revenda = useQuery({ queryKey: ['revenda'], queryFn: () => chamarApi<MinhaRevenda>('/revenda'), enabled: dono })
  return dono && !!revenda.data?.revendedor.encerradaEm
}
