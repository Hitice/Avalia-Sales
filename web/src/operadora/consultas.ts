import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { chamarApi, chamarResposta, enviarJson } from '@/api/cliente'
import type { Cupom, EnvioAoErp, Lote, Pacote, Pedido, Revendedor } from '@/api/tipos'

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
export const useIniciarProducao = () => useGravar((id: string) => chamarApi(`/operadora/pedidos/${id}/producao`, enviarJson('POST', {})))
export const useDespachar = () => useGravar(({ id, lote, retirada }: { id: string; lote: number; retirada: boolean }) => chamarApi(`/operadora/pedidos/${id}/despacho`, enviarJson('POST', { lote, retirada })))
export const useGerarLote = () => useGravar((quantidade: number) => chamarApi<Lote>('/operadora/lotes', enviarJson('POST', { quantidade })))
export const useCriarPacote = () => useGravar((p: { quantidade: number; precoCents: number }) => chamarApi('/operadora/pacotes', enviarJson('POST', p)))
export const useAlterarPacote = () => useGravar(({ id, ...p }: { id: string; quantidade: number; precoCents: number }) => chamarApi(`/operadora/pacotes/${id}`, enviarJson('PUT', p)))
export const useAtivarPacote = () => useGravar(({ id, ativo }: { id: string; ativo: boolean }) => chamarApi(`/operadora/pacotes/${id}/ativo`, enviarJson('PUT', { ativo })))
export const useExcluirPacote = () => useGravar((id: string) => chamarApi(`/operadora/pacotes/${id}`, { method: 'DELETE' }))
export const useDefinirRecebedor = () => useGravar(({ id, recebedor }: { id: string; recebedor: string }) => chamarApi(`/operadora/revendedores/${id}/recebedor`, enviarJson('PUT', { recebedor })))
export const useContratoAssinado = () => useGravar((id: string) => chamarApi(`/operadora/revendedores/${id}/contrato-assinado`, enviarJson('POST', {})))

/** Aprova ou recusa a reabertura da revenda encerrada pelo distrato. */
export const useReabertura = () => useGravar(({ id, aprovar }: { id: string; aprovar: boolean }) =>
  chamarApi(`/operadora/revendedores/${id}/reabertura`, aprovar ? enviarJson('POST', {}) : { method: 'DELETE' }))

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

export type GravarCupom = { codigo: string; descontoBps: number | null; descontoCents: number | null; validoAte: string | null; usosMaximos: number | null }
export const useCupons = () => useQuery({ queryKey: ['operadora', 'cupons'], queryFn: () => chamarApi<Cupom[]>('/operadora/cupons') })
export const useCriarCupom = () => useGravar((c: GravarCupom) => chamarApi('/operadora/cupons', enviarJson('POST', c)))
export const useAlterarCupom = () => useGravar(({ id, ...c }: GravarCupom & { id: string }) => chamarApi(`/operadora/cupons/${id}`, enviarJson('PUT', c)))
export const useAtivarCupom = () => useGravar(({ id, ativo }: { id: string; ativo: boolean }) => chamarApi(`/operadora/cupons/${id}/ativo`, enviarJson('PUT', { ativo })))
export const useExcluirCupom = () => useGravar((id: string) => chamarApi(`/operadora/cupons/${id}`, { method: 'DELETE' }))

export const useFilaDoErp = () => useQuery({ queryKey: ['operadora', 'erp'], queryFn: () => chamarApi<EnvioAoErp[]>('/operadora/erp') })
export const useReenviarAoErp = () => useGravar((pedidoId: string) => chamarApi(`/operadora/erp/${pedidoId}/reenvio`, enviarJson('POST', {})))
