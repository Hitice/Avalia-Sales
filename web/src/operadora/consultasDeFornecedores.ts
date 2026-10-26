import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { chamarApi, enviarJson } from '@/api/cliente'
import type { Fornecedor, FuncaoDoFornecedor, Pagina } from '@/api/tipos'
import { useListaPaginada } from '@/lib/paginacao'

export type GravarFornecedor = { nome: string; documento: string | null; contato: string | null; funcoes: FuncaoDoFornecedor[]; observacao: string | null }

export const useFornecedores = (funcao: FuncaoDoFornecedor | undefined) =>
  useListaPaginada<Fornecedor>(['operadora', 'fornecedores', 'lista', funcao], '/operadora/fornecedores', { filtros: { funcao } })

/** Os fornecedores ativos para escolher numa despesa ou num custo; a seleção cobre até 100. */
export const useFornecedoresAtivos = () =>
  useQuery({
    queryKey: ['operadora', 'fornecedores', 'ativos'],
    queryFn: async () => (await chamarApi<Pagina<Fornecedor>>('/operadora/fornecedores?ativos=true&tamanho=100')).itens,
  })

function useGravar<T>(fazer: (dados: T) => Promise<unknown>) {
  const cliente = useQueryClient()
  return useMutation({ mutationFn: fazer, meta: { sucesso: 'Alteração salva.' }, onSuccess: () => cliente.invalidateQueries({ queryKey: ['operadora'] }) })
}

export const useSalvarFornecedor = () =>
  useGravar(({ id, ...f }: GravarFornecedor & { id?: string }) =>
    chamarApi<Fornecedor>(id ? `/operadora/fornecedores/${id}` : '/operadora/fornecedores', enviarJson(id ? 'PUT' : 'POST', f)))
export const useAtivarFornecedor = () => useGravar(({ id, ativo }: { id: string; ativo: boolean }) => chamarApi(`/operadora/fornecedores/${id}/ativo`, enviarJson('PUT', { ativo })))
export const useExcluirFornecedor = () => useGravar((id: string) => chamarApi(`/operadora/fornecedores/${id}`, { method: 'DELETE' }))
