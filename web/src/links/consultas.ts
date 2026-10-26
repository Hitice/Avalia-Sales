import { useMutation, useQueryClient } from '@tanstack/react-query'
import { chamarApi, enviarJson } from '@/api/cliente'
import type { DestinoInformado, LinkCurto } from '@/api/tipos'
import { useListaPaginada } from '@/lib/paginacao'
import { useAtualizarLimiteDeDestinos } from '@/painel/consultasDoLimite'

export type GravarLink = { titulo: string; destino: DestinoInformado }

const chave = ['links'] as const

/** Os links em páginas; `soAtivos` deixa só os ligados (a gravação em tag NFC). */
export const useLinks = (soAtivos = false) => useListaPaginada<LinkCurto>([...chave, soAtivos ? 'ativos' : 'todos'], '/links', { filtros: { ativos: soAtivos || undefined } })

function useGravar<T>(fazer: (dados: T) => Promise<unknown>) {
  const cliente = useQueryClient()
  const atualizarLimite = useAtualizarLimiteDeDestinos()
  // Criar e alterar link contam no limite diário de destinos; as outras gravações só pedem a contagem de novo, sem custo.
  return useMutation({ mutationFn: fazer, meta: { sucesso: 'Alteração salva.' }, onSuccess: () => Promise.all([cliente.invalidateQueries({ queryKey: chave }), atualizarLimite()]) })
}

export const useCriarLink = () => useGravar((l: GravarLink) => chamarApi<LinkCurto>('/links', enviarJson('POST', l)))
export const useAlterarLink = () => useGravar(({ id, ...l }: GravarLink & { id: string }) => chamarApi<LinkCurto>(`/links/${id}`, enviarJson('PUT', l)))
export const useAtivarLink = () => useGravar(({ id, ativo }: { id: string; ativo: boolean }) => chamarApi(`/links/${id}/ativo`, enviarJson('PUT', { ativo })))
export const useExcluirLink = () => useGravar((id: string) => chamarApi(`/links/${id}`, { method: 'DELETE' }))
