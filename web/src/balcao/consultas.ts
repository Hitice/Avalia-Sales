import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { chamarApi, enviarJson } from '@/api/cliente'
import type { components } from '@/api/esquema'
import type { DestinoInformado, Ficha, Lugar, NovaVendaComItens, RenovacaoAberta, VendasRegistradas } from '@/api/tipos'
import type { ConfiguracaoDoCartao } from '@/lib/cartao'


/** A ficha da placa; refeita a cada 5 s enquanto `acompanhar` estiver ligado (esperando o Pix). */
export function useFicha(codigo: string | undefined, acompanhar = false) {
  return useQuery({
    queryKey: ['placa', codigo],
    queryFn: () => chamarApi<Ficha>(`/placas/${codigo}`),
    enabled: !!codigo,
    retry: false,
    refetchInterval: acompanhar ? 5_000 : false,
  })
}

type PagamentoDaVenda = components['schemas']['PagamentoDaVenda']

/** O pagamento da venda (placa ou serviço), refeito a cada 5 s enquanto a tela espera o Pix. */
export function usePagamentoDaVenda(vendaId: string, acompanhar: boolean) {
  return useQuery({
    queryKey: ['vendas', vendaId, 'pagamento'],
    queryFn: () => chamarApi<PagamentoDaVenda>(`/vendas/${vendaId}/pagamento`),
    refetchInterval: acompanhar ? 5_000 : false,
  })
}

/** A venda com vários itens numa cobrança só; cada placa ou serviço vira uma venda do mesmo grupo. */
export function useVenderItens() {
  const cliente = useQueryClient()
  return useMutation({
    mutationFn: (venda: NovaVendaComItens) => chamarApi<VendasRegistradas>('/vendas/itens', enviarJson('POST', venda)),
    onSuccess: () => Promise.all(['vendas', 'placas', 'placa', 'servicos', 'clientes', 'financeiro'].map((c) => cliente.invalidateQueries({ queryKey: [c] }))),
  })
}

export function useBuscarLugar() {
  return useMutation({ mutationFn: (texto: string) => chamarApi<Lugar[]>(`/lugares?texto=${encodeURIComponent(texto)}`) })
}

export function useApontar(codigo: string) {
  const cliente = useQueryClient()
  return useMutation({
    mutationFn: (destino: DestinoInformado) => chamarApi(`/placas/${codigo}/destino`, enviarJson('PUT', destino)),
    onSuccess: () => cliente.invalidateQueries({ queryKey: ['placa', codigo] }),
  })
}

export function useAlternar(codigo: string) {
  const cliente = useQueryClient()
  return useMutation({
    mutationFn: () => chamarApi(`/placas/${codigo}/alternar`, enviarJson('POST', {})),
    onSuccess: () => Promise.all([cliente.invalidateQueries({ queryKey: ['placa', codigo] }), cliente.invalidateQueries({ queryKey: ['placas'] })]),
  })
}

/** Renovação recebida por fora registrada por engano: o revendedor desfaz, com estorno no razão. */
export function useDesfazerRenovacao(codigo: string) {
  const cliente = useQueryClient()
  return useMutation({
    mutationFn: () => chamarApi(`/placas/${codigo}/renovacao/recebimento`, { method: 'DELETE' }),
    meta: { sucesso: 'Renovação desfeita.' },
    onSuccess: () => Promise.all(REFAZ_A_RENOVACAO.map((chave) => cliente.invalidateQueries({ queryKey: chave === 'placa' ? ['placa', codigo] : [chave] }))),
  })
}

/** A renovação recebida por fora (ou desfeita) entra no razão e na comissão da venda: a ficha, a lista de vendas e o resultado do mês acompanham. */
const REFAZ_A_RENOVACAO = ['placa', 'razao', 'financeiro', 'comissoes', 'vendas']

export function useRenovar(codigo: string) {
  const cliente = useQueryClient()
  return useMutation({
    mutationFn: (porFora?: 'Dinheiro' | 'OutroPix') =>
      porFora
        ? chamarApi<RenovacaoAberta>(`/placas/${codigo}/renovacao/recebimento`, enviarJson('POST', { meio: porFora }))
        : chamarApi<RenovacaoAberta>(`/placas/${codigo}/renovacao`, enviarJson('POST', {})),
    onSuccess: (_aberta, porFora) =>
      Promise.all((porFora ? REFAZ_A_RENOVACAO : ['placa']).map((chave) => cliente.invalidateQueries({ queryKey: chave === 'placa' ? ['placa', codigo] : [chave] }))),
  })
}

/** Se a revenda vende no cartão, e com que chave pública o navegador tokeniza. */
export const useConfiguracaoDoCartao = () =>
  useQuery({ queryKey: ['cartao'], queryFn: () => chamarApi<ConfiguracaoDoCartao>('/pagamento/cartao'), staleTime: 10 * 60_000 })
