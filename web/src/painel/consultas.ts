import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { chamarApi, enviarJson } from '@/api/cliente'
import type { EstadoDaPlaca, LoteDaRevenda, MinhaRevenda, MotivoDaBaixa, MotivoDoCancelamento, PlacaDaVendaCancelada, Pacote, PaginaDePlacas, Pedido, Repasse, ResumoDoMes, DetalheDaVenda, ResumoDoVendedor, PlacaDaCarteira, SaldoDeComissao, PendenteDeConferencia, LimitesDePorFora, VendaDaLista, VendaRegistrada, Vendedor } from '@/api/tipos'

export type FiltroDePlacas = { estado?: EstadoDaPlaca; consignadaA?: string; codigo?: string; pagina?: number }

export const useMinhaRevenda = () => useQuery({ queryKey: ['revenda'], queryFn: () => chamarApi<MinhaRevenda>('/revenda') })
export const usePacotes = () => useQuery({ queryKey: ['pacotes'], queryFn: () => chamarApi<Pacote[]>('/pacotes') })
export const usePedidos = () => useQuery({ queryKey: ['pedidos'], queryFn: () => chamarApi<Pedido[]>('/pedidos') })
export const useEquipe = () => useQuery({ queryKey: ['equipe'], queryFn: () => chamarApi<Vendedor[]>('/equipe') })
export const useVendas = () => useQuery({ queryKey: ['vendas'], queryFn: () => chamarApi<VendaDaLista[]>('/vendas') })
export const usePainelDoVendedor = () => useQuery({ queryKey: ['vendas', 'painel'], queryFn: () => chamarApi<ResumoDoVendedor>('/painel-do-vendedor') })
export const useCarteira = () => useQuery({ queryKey: ['vendas', 'carteira'], queryFn: () => chamarApi<PlacaDaCarteira[]>('/carteira') })
export const useVenda = (id: string) => useQuery({ queryKey: ['vendas', id], queryFn: () => chamarApi<DetalheDaVenda>(`/vendas/${id}`) })

export function usePlacas(filtro: FiltroDePlacas) {
  const busca = new URLSearchParams(Object.entries(filtro).filter(([, v]) => v !== undefined && v !== '').map(([k, v]) => [k, String(v)]))
  return useQuery({ queryKey: ['placas', filtro], queryFn: () => chamarApi<PaginaDePlacas>(`/placas?${busca}`) })
}

/** Gravação que refaz as listas afetadas: placa, pedido, equipe e venda se tocam. */
function useGravar<T, R = unknown>(fazer: (dados: T) => Promise<R>, refazer: string[], sucesso = 'Alteração salva.') {
  const cliente = useQueryClient()
  return useMutation({
    mutationFn: fazer,
    meta: { sucesso },
    onSuccess: () => Promise.all(refazer.map((chave) => cliente.invalidateQueries({ queryKey: [chave] }))),
  })
}

export const usePedir = () => useGravar(({ pacoteId, cupom }: { pacoteId: string; cupom: string | null }) => chamarApi<Pedido>('/pedidos', enviarJson('POST', { pacoteId, cupom })), ['pedidos'], 'Pedido feito.')
export const useCancelarPedido = () => useGravar((id: string) => chamarApi(`/pedidos/${id}/cancelamento`, enviarJson('POST', {})), ['pedidos'], 'Pedido cancelado.')
export const useAtivarLote = () =>
  useGravar((numero: string) => chamarApi<{ lote: string; placas: number; primeiroCodigo: string; ultimoCodigo: string }>('/lotes/ativacao', enviarJson('POST', { numero })), ['pedidos', 'placas', 'lotes'], 'Lote ativado.')

export type GravarVendedor = { nome: string; telefone: string | null; cpf: string | null; chavePix: string | null; comissaoBps: number | null; metaMensalCents: number | null }
export const useCriarVendedor = () => useGravar((v: GravarVendedor & { email: string; senha: string | null }) => chamarApi<Vendedor>('/equipe', enviarJson('POST', v)), ['equipe'], 'Vendedor cadastrado.')
export const useAlterarVendedor = () => useGravar(({ id, ...v }: GravarVendedor & { id: string }) => chamarApi<Vendedor>(`/equipe/${id}`, enviarJson('PUT', v)), ['equipe'])
export const useConvidarVendedor = () => useGravar((id: string) => chamarApi<Vendedor>(`/equipe/${id}/convite`, enviarJson('POST')), ['equipe'], 'Convite enviado.')
export const useAtivarVendedor = () => useGravar(({ id, ativo }: { id: string; ativo: boolean }) => chamarApi(`/equipe/${id}/ativo`, enviarJson('PUT', { ativo })), ['equipe'])
export const useExcluirVendedor = () => useGravar((id: string) => chamarApi(`/equipe/${id}`, { method: 'DELETE' }), ['equipe', 'placas'], 'Vendedor excluído.')
export const useComissaoPadrao = () => useGravar((comissaoBps: number) => chamarApi('/revenda/comissao', enviarJson('PUT', { comissaoBps })), ['revenda', 'equipe'])

export const useConsignar = () =>
  useGravar((c: { vendedorId: string; codigos?: string[]; quantidade?: number }) => chamarApi<{ placas: number }>('/estoque/consignacao', enviarJson('POST', c)), ['placas'], 'Placas consignadas.')
export const useLotesDaRevenda = () => useQuery({ queryKey: ['lotes'], queryFn: () => chamarApi<LoteDaRevenda[]>('/lotes') })
export const useArquivarLoteDaRevenda = () =>
  useGravar(({ id, arquivado }: { id: string; arquivado: boolean }) => chamarApi(`/lotes/${id}/arquivado`, enviarJson('PUT', { arquivado })), ['lotes', 'placas'])
export const useBaixarPlaca = () =>
  useGravar(({ codigo, motivo }: { codigo: string; motivo: MotivoDaBaixa }) => chamarApi(`/placas/${codigo}/baixa`, enviarJson('POST', { motivo })), ['lotes', 'placas'], 'Baixa registrada.')
export const useDesfazerBaixa = () => useGravar((codigo: string) => chamarApi(`/placas/${codigo}/baixa`, { method: 'DELETE' }), ['lotes', 'placas'], 'Baixa desfeita.')
export const useDevolver = () => useGravar((vendedorId: string) => chamarApi<{ placas: number }>('/estoque/devolucao', enviarJson('POST', { vendedorId })), ['placas'], 'Placas devolvidas ao estoque.')

export const useCobrarDeNovo = () => useGravar((vendaId: string) => chamarApi<VendaRegistrada>(`/vendas/${vendaId}/cobranca`, enviarJson('POST', {})), ['vendas'], 'Cobrança gerada.')

/** Uma venda que muda de estado mexe na lista, na placa e na ficha dela, na comissão, no razão e no resultado do mês (DRE e caixa leem o razão e as vendas). */
const VENDA_MUDOU = ['vendas', 'placas', 'placa', 'comissoes', 'razao', 'financeiro']
export const useReceberPorFora = () =>
  useGravar(({ vendaId, meio }: { vendaId: string; meio: 'Dinheiro' | 'OutroPix' }) => chamarApi(`/vendas/${vendaId}/recebimento`, enviarJson('POST', { meio })), VENDA_MUDOU, 'Recebimento registrado.')
export const useDesfazerRecebimento = () => useGravar((vendaId: string) => chamarApi(`/vendas/${vendaId}/recebimento`, { method: 'DELETE' }), VENDA_MUDOU, 'Recebimento desfeito.')
export type CancelarVenda = { vendaId: string; motivo: MotivoDoCancelamento; placa: PlacaDaVendaCancelada; observacao: string | null }
export const useCancelarVenda = () =>
  useGravar(({ vendaId, ...c }: CancelarVenda) => chamarApi(`/vendas/${vendaId}/cancelamento`, enviarJson('POST', c)), [...VENDA_MUDOU, 'lotes'], 'Venda cancelada.')
export const useCortesia = () => useGravar((vendaId: string) => chamarApi(`/vendas/${vendaId}/cortesia`, enviarJson('POST', {})), VENDA_MUDOU, 'Venda dada de cortesia.')
export const useTrocarPlaca = () =>
  useGravar(({ vendaId, codigo, motivo }: { vendaId: string; codigo: string; motivo: MotivoDaBaixa }) =>
    chamarApi<{ codigo: string }>(`/vendas/${vendaId}/troca-de-placa`, enviarJson('POST', { codigo, motivo })), [...VENDA_MUDOU, 'lotes'], 'Placa trocada.')

export const useSaldosDeComissao = () => useQuery({ queryKey: ['comissoes'], queryFn: () => chamarApi<SaldoDeComissao[]>('/comissoes') })
export const usePendentesDeConferencia = () => useQuery({ queryKey: ['comissoes', 'conferencia'], queryFn: () => chamarApi<PendenteDeConferencia[]>('/comissoes/conferencia') })
/** Sem lista, o dono confere todos os pendentes. */
export const useConferirRecebimentos = () =>
  useGravar((cobrancaIds: string[] | null) => chamarApi<{ conferidos: number }>('/comissoes/conferencia', enviarJson('POST', { cobrancaIds })), ['comissoes', 'auditoria'], 'Recebimentos conferidos.')
export const useDefinirLimitesPorFora = () =>
  useGravar(({ id, ...limites }: LimitesDePorFora & { id: string }) => chamarApi(`/equipe/${id}/limites-por-fora`, enviarJson('PUT', limites)), ['equipe', 'auditoria'], 'Limites salvos.')
export const useRepasses = () => useQuery({ queryKey: ['repasses'], queryFn: () => chamarApi<Repasse[]>('/repasses') })
export const useRepassar = () =>
  useGravar((r: { vendedorId: string; ate: string | null; comprovante: string | null }) => chamarApi<Repasse>('/repasses', enviarJson('POST', r)), ['comissoes', 'repasses', 'razao', 'financeiro'], 'Repasse registrado.')
export const useCorrigirVenda = () =>
  useGravar(({ vendaId, ...c }: { vendaId: string; valorCents: number | null; vendedorId: string | null }) => chamarApi(`/vendas/${vendaId}`, enviarJson('PUT', c)), [...VENDA_MUDOU, 'comissoes', 'razao'], 'Venda corrigida.')
export const useDesfazerRepasse = () => useGravar((id: string) => chamarApi(`/repasses/${id}`, { method: 'DELETE' }), ['comissoes', 'repasses', 'razao', 'financeiro', 'vendas'], 'Repasse desfeito.')
export const useRazao = (competencia: string | undefined) =>
  useQuery({ queryKey: ['razao', competencia], queryFn: () => chamarApi<ResumoDoMes>(`/razao${competencia ? `?competencia=${competencia}` : ''}`) })
export const useMesesDoRazao = () => useQuery({ queryKey: ['razao', 'meses'], queryFn: () => chamarApi<string[]>('/razao/meses') })
export const usePagarPedido = () => useGravar((id: string) => chamarApi<{ url: string }>(`/pedidos/${id}/pagamento`, enviarJson('POST', {})), ['pedidos'])
