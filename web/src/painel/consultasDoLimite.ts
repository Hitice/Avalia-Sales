import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { chamarApi, enviarJson } from '@/api/cliente'
import type { SituacaoDoLimite } from '@/api/tipos'

const chave = ['limite-de-destinos'] as const

/** Quantos cadastros de destino a revenda já fez hoje, de quantos pode. */
export const useLimiteDeDestinos = () => useQuery({ queryKey: chave, queryFn: () => chamarApi<SituacaoDoLimite>('/limite-de-destinos') })

/** Refaz a contagem depois de vender placa com destino ou de criar ou alterar link. */
export function useAtualizarLimiteDeDestinos() {
  const cliente = useQueryClient()
  return () => cliente.invalidateQueries({ queryKey: chave })
}

/** Só o dono pede: abre um chamado para a Avalia liberar mais cadastros no dia. */
export const useSolicitarLiberacaoDeDestinos = () =>
  useMutation({ mutationFn: (motivo: string) => chamarApi('/revenda/limite-de-destinos/solicitacao', enviarJson('POST', { motivo: motivo.trim() || null })) })
