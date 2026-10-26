import { useMutation, useQueryClient } from '@tanstack/react-query'
import { chamarApi, enviarJson } from '@/api/cliente'
import type { DestinoBloqueado } from '@/api/tipos'
import { useListaPaginada } from '@/lib/paginacao'

export const useDestinosBloqueados = () => useListaPaginada<DestinoBloqueado>(['operadora', 'destinos-bloqueados'], '/operadora/destinos-bloqueados')

function useGravar<T>(fazer: (dados: T) => Promise<unknown>) {
  const cliente = useQueryClient()
  return useMutation({ mutationFn: fazer, meta: { sucesso: 'Alteração salva.' }, onSuccess: () => cliente.invalidateQueries({ queryKey: ['operadora'] }) })
}

export const useBloquearDestino = () => useGravar((d: { dominio: string; motivo: string }) => chamarApi<DestinoBloqueado>('/operadora/destinos-bloqueados', enviarJson('POST', d)))
export const useDesbloquearDestino = () => useGravar((id: string) => chamarApi(`/operadora/destinos-bloqueados/${id}`, { method: 'DELETE' }))
