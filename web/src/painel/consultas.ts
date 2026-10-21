import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { chamarApi, enviarJson } from '@/api/cliente'
import type { EstadoDaPlaca, MinhaRevenda, Pacote, PaginaDePlacas, Pedido, VendaDaLista, VendaRegistrada, Vendedor } from '@/api/tipos'

export type FiltroDePlacas = { estado?: EstadoDaPlaca; consignadaA?: string; codigo?: string; pagina?: number }

export const useMinhaRevenda = () => useQuery({ queryKey: ['revenda'], queryFn: () => chamarApi<MinhaRevenda>('/revenda') })
export const usePacotes = () => useQuery({ queryKey: ['pacotes'], queryFn: () => chamarApi<Pacote[]>('/pacotes') })
export const usePedidos = () => useQuery({ queryKey: ['pedidos'], queryFn: () => chamarApi<Pedido[]>('/pedidos') })
export const useEquipe = () => useQuery({ queryKey: ['equipe'], queryFn: () => chamarApi<Vendedor[]>('/equipe') })
export const useVendas = () => useQuery({ queryKey: ['vendas'], queryFn: () => chamarApi<VendaDaLista[]>('/vendas') })

export function usePlacas(filtro: FiltroDePlacas) {
  const busca = new URLSearchParams(Object.entries(filtro).filter(([, v]) => v !== undefined && v !== '').map(([k, v]) => [k, String(v)]))
  return useQuery({ queryKey: ['placas', filtro], queryFn: () => chamarApi<PaginaDePlacas>(`/placas?${busca}`) })
}

/** Gravação que refaz as listas afetadas: placa, pedido, equipe e venda se tocam. */
function useGravar<T, R = unknown>(fazer: (dados: T) => Promise<R>, refazer: string[]) {
  const cliente = useQueryClient()
  return useMutation({
    mutationFn: fazer,
    onSuccess: () => Promise.all(refazer.map((chave) => cliente.invalidateQueries({ queryKey: [chave] }))),
  })
}

export const usePedir = () => useGravar((pacoteId: string) => chamarApi<Pedido>('/pedidos', enviarJson('POST', { pacoteId })), ['pedidos'])
export const useCancelarPedido = () => useGravar((id: string) => chamarApi(`/pedidos/${id}/cancelamento`, enviarJson('POST', {})), ['pedidos'])
export const useAtivarLote = () =>
  useGravar((numero: string) => chamarApi<{ lote: string; placas: number; primeiroCodigo: string; ultimoCodigo: string }>('/lotes/ativacao', enviarJson('POST', { numero })), ['pedidos', 'placas'])

export type GravarVendedor = { nome: string; telefone: string | null; chavePix: string | null; comissaoBps: number | null }
export const useCriarVendedor = () => useGravar((v: GravarVendedor & { email: string; senha: string }) => chamarApi<Vendedor>('/equipe', enviarJson('POST', v)), ['equipe'])
export const useAlterarVendedor = () => useGravar(({ id, ...v }: GravarVendedor & { id: string }) => chamarApi<Vendedor>(`/equipe/${id}`, enviarJson('PUT', v)), ['equipe'])
export const useAtivarVendedor = () => useGravar(({ id, ativo }: { id: string; ativo: boolean }) => chamarApi(`/equipe/${id}/ativo`, enviarJson('PUT', { ativo })), ['equipe'])
export const useExcluirVendedor = () => useGravar((id: string) => chamarApi(`/equipe/${id}`, { method: 'DELETE' }), ['equipe', 'placas'])
export const useComissaoPadrao = () => useGravar((comissaoBps: number) => chamarApi('/revenda/comissao', enviarJson('PUT', { comissaoBps })), ['revenda', 'equipe'])

export const useConsignar = () =>
  useGravar((c: { vendedorId: string; codigos?: string[]; quantidade?: number }) => chamarApi<{ placas: number }>('/estoque/consignacao', enviarJson('POST', c)), ['placas'])
export const useDevolver = () => useGravar((vendedorId: string) => chamarApi<{ placas: number }>('/estoque/devolucao', enviarJson('POST', { vendedorId })), ['placas'])

export const useCobrarDeNovo = () => useGravar((vendaId: string) => chamarApi<VendaRegistrada>(`/vendas/${vendaId}/cobranca`, enviarJson('POST', {})), ['vendas'])
