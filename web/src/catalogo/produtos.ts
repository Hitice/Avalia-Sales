import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { chamarApi, enviarJson } from '@/api/cliente'

import type { components } from '@/api/esquema'

export type Produto = components['schemas']['ProdutoDto']
export type NovoProduto = components['schemas']['CadastrarProduto']

const chave = ['produtos'] as const

export function useProdutos() {
  return useQuery({ queryKey: chave, queryFn: () => chamarApi<Produto[]>('/produtos') })
}

function useGravar<T>(fazer: (dados: T) => Promise<unknown>) {
  const cliente = useQueryClient()
  return useMutation({ mutationFn: fazer, onSuccess: () => cliente.invalidateQueries({ queryKey: chave }) })
}

export const useCadastrarProduto = () => useGravar((p: NovoProduto) => chamarApi<Produto>('/produtos', enviarJson('POST', p)))
export const useAlterarProduto = () => useGravar(({ id, ...p }: NovoProduto & { id: string }) => chamarApi<Produto>(`/produtos/${id}`, enviarJson('PUT', p)))
export const useAtivarProduto = () => useGravar(({ id, ativo }: { id: string; ativo: boolean }) => chamarApi(`/produtos/${id}/ativo`, enviarJson('PUT', { ativo })))
export const useExcluirProduto = () => useGravar((id: string) => chamarApi(`/produtos/${id}`, { method: 'DELETE' }))
