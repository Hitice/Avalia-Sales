import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { chamarApi, enviarJson } from '@/api/cliente'
import type { ItemDoCatalogo } from '@/api/tipos'

const chave = ['catalogo'] as const

/** O catálogo da Avalia com o preço da revenda: o dono ajusta, a equipe lê para vender. */
export function useCatalogo() {
  return useQuery({ queryKey: chave, queryFn: () => chamarApi<ItemDoCatalogo[]>('/catalogo') })
}

function useGravar<T>(fazer: (dados: T) => Promise<unknown>) {
  const cliente = useQueryClient()
  return useMutation({ mutationFn: fazer, meta: { sucesso: 'Alteração salva.' }, onSuccess: () => cliente.invalidateQueries({ queryKey: chave }) })
}

export const useAjustarProduto = () =>
  useGravar(({ id, ...p }: { id: string; precoCents: number; comissaoBps: number | null }) => chamarApi<ItemDoCatalogo>(`/catalogo/${id}`, enviarJson('PUT', p)))
export const useAtivarProduto = () =>
  useGravar(({ id, ativo }: { id: string; ativo: boolean }) => chamarApi(`/catalogo/${id}/ativo`, enviarJson('PUT', { ativo })))

/** Leva placa do estoque na venda: a Nova venda passa pelo QR. */
export const usaPlaca = (p: ItemDoCatalogo) => p.tipo === 'Placa' || p.tipo === 'PlacaComServico'

/** A placa personalizada: serviço com um destino por QR, feita depois da venda (ADR 0032). */
export const personalizada = (p: ItemDoCatalogo) => p.tipo === 'Servico' && (p.codigosNaPlaca ?? 0) > 0
