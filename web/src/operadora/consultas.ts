import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { chamarApi, chamarResposta, enviarJson } from '@/api/cliente'
import type { Lote, Pacote, Pedido, Revendedor } from '@/api/tipos'

const chaves = {
  pedidos: ['operadora', 'pedidos'] as const,
  lotes: ['operadora', 'lotes'] as const,
  pacotes: ['operadora', 'pacotes'] as const,
  revendedores: ['operadora', 'revendedores'] as const,
}

export const usePedidosDaOperadora = () => useQuery({ queryKey: chaves.pedidos, queryFn: () => chamarApi<Pedido[]>('/operadora/pedidos') })
export const useLotes = () => useQuery({ queryKey: chaves.lotes, queryFn: () => chamarApi<Lote[]>('/operadora/lotes') })
export const usePacotesDaOperadora = () => useQuery({ queryKey: chaves.pacotes, queryFn: () => chamarApi<Pacote[]>('/operadora/pacotes') })
export const useRevendedores = () => useQuery({ queryKey: chaves.revendedores, queryFn: () => chamarApi<Revendedor[]>('/operadora/revendedores') })

/** Toda gravação da operadora refaz as listas dela: pedido, lote e revendedor andam juntos. */
function useGravar<T>(fazer: (dados: T) => Promise<unknown>) {
  const cliente = useQueryClient()
  return useMutation({ mutationFn: fazer, onSuccess: () => cliente.invalidateQueries({ queryKey: ['operadora'] }) })
}

export const useRegistrarPagamento = () => useGravar((id: string) => chamarApi(`/operadora/pedidos/${id}/pagamento`, enviarJson('POST', {})))
export const useDespachar = () => useGravar(({ id, lote }: { id: string; lote: number }) => chamarApi(`/operadora/pedidos/${id}/despacho`, enviarJson('POST', { lote })))
export const useGerarLote = () => useGravar((quantidade: number) => chamarApi<Lote>('/operadora/lotes', enviarJson('POST', { quantidade })))
export const useCriarPacote = () => useGravar((p: { quantidade: number; precoCents: number }) => chamarApi('/operadora/pacotes', enviarJson('POST', p)))
export const useAlterarPacote = () => useGravar(({ id, ...p }: { id: string; quantidade: number; precoCents: number }) => chamarApi(`/operadora/pacotes/${id}`, enviarJson('PUT', p)))
export const useAtivarPacote = () => useGravar(({ id, ativo }: { id: string; ativo: boolean }) => chamarApi(`/operadora/pacotes/${id}/ativo`, enviarJson('PUT', { ativo })))
export const useExcluirPacote = () => useGravar((id: string) => chamarApi(`/operadora/pacotes/${id}`, { method: 'DELETE' }))
export const useDefinirRecebedor = () => useGravar(({ id, recebedor }: { id: string; recebedor: string }) => chamarApi(`/operadora/revendedores/${id}/recebedor`, enviarJson('PUT', { recebedor })))
export const useContratoAssinado = () => useGravar((id: string) => chamarApi(`/operadora/revendedores/${id}/contrato-assinado`, enviarJson('POST', {})))

/** O ZIP da gráfica vem com o token, então é baixado pela chamada e entregue ao navegador como arquivo. */
export async function baixarImpressao(lote: Lote) {
  const resposta = await chamarResposta(`/operadora/lotes/${lote.id}/impressao`)
  const url = URL.createObjectURL(await resposta.blob())
  const link = document.createElement('a')
  link.href = url
  link.download = `lote-${lote.rotulo.toLowerCase()}.zip`
  link.click()
  URL.revokeObjectURL(url)
}
