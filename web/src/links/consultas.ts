import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { chamarApi, enviarJson } from '@/api/cliente'
import type { DestinoInformado, LinkCurto } from '@/api/tipos'

export type GravarLink = { titulo: string; destino: DestinoInformado }

const chave = ['links'] as const

export const useLinks = () => useQuery({ queryKey: chave, queryFn: () => chamarApi<LinkCurto[]>('/links') })

function useGravar<T>(fazer: (dados: T) => Promise<unknown>) {
  const cliente = useQueryClient()
  return useMutation({ mutationFn: fazer, meta: { sucesso: 'Alteração salva.' }, onSuccess: () => cliente.invalidateQueries({ queryKey: chave }) })
}

export const useCriarLink = () => useGravar((l: GravarLink) => chamarApi<LinkCurto>('/links', enviarJson('POST', l)))
export const useAlterarLink = () => useGravar(({ id, ...l }: GravarLink & { id: string }) => chamarApi<LinkCurto>(`/links/${id}`, enviarJson('PUT', l)))
export const useAtivarLink = () => useGravar(({ id, ativo }: { id: string; ativo: boolean }) => chamarApi(`/links/${id}/ativo`, enviarJson('PUT', { ativo })))
export const useExcluirLink = () => useGravar((id: string) => chamarApi(`/links/${id}`, { method: 'DELETE' }))
