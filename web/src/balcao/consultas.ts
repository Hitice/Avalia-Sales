import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { chamarApi, enviarJson } from '@/api/cliente'
import type { components } from '@/api/esquema'
import type { DestinoInformado, Ficha, Lugar, RenovacaoAberta, VendaRegistrada } from '@/api/tipos'
import type { ConfiguracaoDoCartao } from '@/lib/cartao'

export type NovaVenda = components['schemas']['NovaVenda']

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

export function useVender() {
  const cliente = useQueryClient()
  return useMutation({
    mutationFn: (venda: NovaVenda) => chamarApi<VendaRegistrada>('/vendas', enviarJson('POST', venda)),
    onSuccess: () => Promise.all(['vendas', 'placas', 'placa'].map((c) => cliente.invalidateQueries({ queryKey: [c] }))),
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

export function useRenovar(codigo: string) {
  const cliente = useQueryClient()
  return useMutation({
    mutationFn: (porFora?: 'Dinheiro' | 'OutroPix') =>
      porFora
        ? chamarApi<RenovacaoAberta>(`/placas/${codigo}/renovacao/recebimento`, enviarJson('POST', { meio: porFora }))
        : chamarApi<RenovacaoAberta>(`/placas/${codigo}/renovacao`, enviarJson('POST', {})),
    onSuccess: () => cliente.invalidateQueries({ queryKey: ['placa', codigo] }),
  })
}

/** Se a revenda vende no cartão, e com que chave pública o navegador tokeniza. */
export const useConfiguracaoDoCartao = () =>
  useQuery({ queryKey: ['cartao'], queryFn: () => chamarApi<ConfiguracaoDoCartao>('/pagamento/cartao'), staleTime: 10 * 60_000 })
