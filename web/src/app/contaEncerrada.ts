import { useSessao } from '@/acesso/sessao'
import { useMinhaRevenda } from '@/painel/consultas'

/** Telas que só servem para vender ou mexer na conta: somem da revenda encerrada pelo distrato. */
const SO_COM_A_CONTA_ATIVA = ['/vender', '/links', '/catalogo', '/painel/mapa']

export const soComAContaAtiva = (caminho: string) => SO_COM_A_CONTA_ATIVA.some((t) => caminho === t || caminho.startsWith(`${t}/`))

/** A revenda do dono logado, se encerrada pelo distrato (a API recusa a escrita; a tela esconde o que só serve para vender). */
export function useContaEncerrada() {
  const dono = useSessao((s) => s.perfil?.papel === 'Dono')
  const revenda = useMinhaRevenda(dono)
  return dono && !!revenda.data?.revendedor.encerradaEm
}
