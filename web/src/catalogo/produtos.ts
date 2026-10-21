import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { chamarApi } from '@/api/cliente'

export type Produto = { id: string; nome: string; precoCents: number; ativo: boolean }
export type NovoProduto = { nome: string; precoCents: number }

const chave = ['produtos'] as const

export function useProdutos() {
  return useQuery({ queryKey: chave, queryFn: () => chamarApi<Produto[]>('/produtos') })
}

export function useCadastrarProduto() {
  const cliente = useQueryClient()
  return useMutation({
    mutationFn: (produto: NovoProduto) => chamarApi<Produto>('/produtos', { method: 'POST', body: JSON.stringify(produto) }),
    onSuccess: () => cliente.invalidateQueries({ queryKey: chave }),
  })
}
