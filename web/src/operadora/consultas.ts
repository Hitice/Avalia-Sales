import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { chamarApi, chamarResposta, enviarJson } from '@/api/cliente'
import type { CategoriaDoLote, Cupom, EnvioAoErp, Lote, Pacote, Pagina, PaginaDeLotes, Pedido, Revendedor } from '@/api/tipos'
import { useListaPaginada } from '@/lib/paginacao'

const chaves = {
  pedidos: ['operadora', 'pedidos'] as const,
  lotes: ['operadora', 'lotes'] as const,
  pacotes: ['operadora', 'pacotes'] as const,
  revendedores: ['operadora', 'revendedores'] as const,
}

export const usePedidosDaOperadora = () => useListaPaginada<Pedido>(chaves.pedidos, '/operadora/pedidos')
/** Sem os arquivados, a menos que a tela peça; `bruto.arquivados` diz quantos ficaram de fora. */
export const useLotes = (arquivados: boolean) =>
  useListaPaginada<Lote, PaginaDeLotes>([...chaves.lotes, 'lista'], '/operadora/lotes', { filtros: { arquivados } })
/** Os lotes gerados e ainda não despachados com a quantidade do pedido, para escolher no despacho. */
export const useLotesLivres = (quantidade: number) =>
  useQuery({ queryKey: [...chaves.lotes, 'livres', quantidade], queryFn: () => chamarApi<Pagina<Lote>>(`/operadora/lotes?livres=true&quantidade=${quantidade}&tamanho=100`) })
export const usePacotesDaOperadora = () => useQuery({ queryKey: chaves.pacotes, queryFn: () => chamarApi<Pacote[]>('/operadora/pacotes') })
export const useRevendedores = () => useListaPaginada<Revendedor>(chaves.revendedores, '/operadora/revendedores')

/** Toda gravação da operadora refaz as listas dela: pedido, lote e revendedor andam juntos. */
function useGravar<T>(fazer: (dados: T) => Promise<unknown>) {
  const cliente = useQueryClient()
  return useMutation({ mutationFn: fazer, meta: { sucesso: 'Alteração salva.' }, onSuccess: () => cliente.invalidateQueries({ queryKey: ['operadora'] }) })
}

export const useRegistrarPagamento = () => useGravar((id: string) => chamarApi(`/operadora/pedidos/${id}/pagamento`, enviarJson('POST', {})))
export const useIniciarProducao = () => useGravar((id: string) => chamarApi(`/operadora/pedidos/${id}/producao`, enviarJson('POST', {})))
export const useDespachar = () => useGravar(({ id, lote, retirada }: { id: string; lote: number; retirada: boolean }) => chamarApi(`/operadora/pedidos/${id}/despacho`, enviarJson('POST', { lote, retirada })))
export type DadosDoLote = { titulo: string | null; categoria: CategoriaDoLote }
export const useGerarLote = () => useGravar((l: DadosDoLote & { quantidade: number }) => chamarApi<Lote>('/operadora/lotes', enviarJson('POST', l)))
export const useDescreverLote = () => useGravar(({ id, ...l }: DadosDoLote & { id: string }) => chamarApi(`/operadora/lotes/${id}`, enviarJson('PUT', l)))
export const useArquivarLote = () => useGravar(({ id, arquivado }: { id: string; arquivado: boolean }) => chamarApi(`/operadora/lotes/${id}/arquivado`, enviarJson('PUT', { arquivado })))
export const useExcluirLote = () => useGravar((id: string) => chamarApi(`/operadora/lotes/${id}`, { method: 'DELETE' }))
export const useCriarPacote = () => useGravar((p: { quantidade: number; precoCents: number }) => chamarApi('/operadora/pacotes', enviarJson('POST', p)))
export const useAlterarPacote = () => useGravar(({ id, ...p }: { id: string; quantidade: number; precoCents: number }) => chamarApi(`/operadora/pacotes/${id}`, enviarJson('PUT', p)))
export const useAtivarPacote = () => useGravar(({ id, ativo }: { id: string; ativo: boolean }) => chamarApi(`/operadora/pacotes/${id}/ativo`, enviarJson('PUT', { ativo })))
export const useExcluirPacote = () => useGravar((id: string) => chamarApi(`/operadora/pacotes/${id}`, { method: 'DELETE' }))
export const useDefinirRenovacao = () => useGravar(({ id, renovacaoBps }: { id: string; renovacaoBps: number }) => chamarApi(`/operadora/revendedores/${id}/renovacao`, enviarJson('PUT', { renovacaoBps })))
export const useDefinirRecebedor = () => useGravar(({ id, recebedor }: { id: string; recebedor: string }) => chamarApi(`/operadora/revendedores/${id}/recebedor`, enviarJson('PUT', { recebedor })))
export const useSuspensao = () => useGravar(({ id, suspender, motivo }: { id: string; suspender: boolean; motivo?: string | null }) =>
  chamarApi(`/operadora/revendedores/${id}/suspensao`, suspender ? enviarJson('POST', { motivo: motivo ?? null }) : { method: 'DELETE' }))
export const useContratoAssinado = () => useGravar((id: string) => chamarApi(`/operadora/revendedores/${id}/contrato-assinado`, enviarJson('POST', {})))

/** Aprova ou recusa a reabertura da revenda encerrada pelo distrato. */
export const useReabertura = () => useGravar(({ id, aprovar }: { id: string; aprovar: boolean }) =>
  chamarApi(`/operadora/revendedores/${id}/reabertura`, aprovar ? enviarJson('POST', {}) : { method: 'DELETE' }))

/** O ZIP da gráfica vem com o token, então é baixado pela chamada e entregue ao navegador como arquivo. */
/** SVG para o Corel, PNG para a mala direta; os dois por padrão. */
export type FormatoDaImpressao = 'SvgEPng' | 'Svg' | 'Png'

export async function baixarImpressao(lote: Lote, formato: FormatoDaImpressao = 'SvgEPng') {
  const resposta = await chamarResposta(`/operadora/lotes/${lote.id}/impressao?formato=${formato}`)
  const url = URL.createObjectURL(await resposta.blob())
  const link = document.createElement('a')
  link.href = url
  link.download = `lote-${lote.rotulo.toLowerCase()}.zip`
  link.click()
  URL.revokeObjectURL(url)
}

export type GravarCupom = { codigo: string; descontoBps: number | null; descontoCents: number | null; validoAte: string | null; usosMaximos: number | null }
export const useCupons = () => useListaPaginada<Cupom>(['operadora', 'cupons'], '/operadora/cupons')
export const useCriarCupom = () => useGravar((c: GravarCupom) => chamarApi('/operadora/cupons', enviarJson('POST', c)))
export const useAlterarCupom = () => useGravar(({ id, ...c }: GravarCupom & { id: string }) => chamarApi(`/operadora/cupons/${id}`, enviarJson('PUT', c)))
export const useAtivarCupom = () => useGravar(({ id, ativo }: { id: string; ativo: boolean }) => chamarApi(`/operadora/cupons/${id}/ativo`, enviarJson('PUT', { ativo })))
export const useExcluirCupom = () => useGravar((id: string) => chamarApi(`/operadora/cupons/${id}`, { method: 'DELETE' }))

export const useFilaDoErp = () => useListaPaginada<EnvioAoErp>(['operadora', 'erp'], '/operadora/erp')
export const useReenviarAoErp = () => useGravar((pedidoId: string) => chamarApi(`/operadora/erp/${pedidoId}/reenvio`, enviarJson('POST', {})))
