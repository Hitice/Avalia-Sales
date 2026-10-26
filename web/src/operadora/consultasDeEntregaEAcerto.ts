import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { chamarApi, enviarJson } from '@/api/cliente'
import type { Acerto, Fornecedor, Pagina, SaldoAAcertar } from '@/api/tipos'
import { useListaPaginada } from '@/lib/paginacao'

/** Toda gravação da operadora refaz as listas dela: pedido, revendedor e financeiro andam juntos. */
function useGravar<T>(fazer: (dados: T) => Promise<unknown>) {
  const cliente = useQueryClient()
  return useMutation({ mutationFn: fazer, meta: { sucesso: 'Alteração salva.' }, onSuccess: () => cliente.invalidateQueries({ queryKey: ['operadora'] }) })
}

export type DadosDoDespacho = { id: string; lote: number; retirada: boolean; transportadora?: string | null; codigoDeRastreio?: string | null }

/** Despacho com a transportadora e o código de rastreio; na retirada a API recusa os dois, então não vão no corpo. */
export const useDespacharComEnvio = () => useGravar(({ id, lote, retirada, transportadora, codigoDeRastreio }: DadosDoDespacho) =>
  chamarApi(`/operadora/pedidos/${id}/despacho`, enviarJson('POST', retirada ? { lote, retirada } : { lote, retirada, transportadora: transportadora || null, codigoDeRastreio: codigoDeRastreio || null })))

/** Só pedido Pago ou em produção; depois do despacho a API recusa. */
export const useEstornarPedido = () => useGravar(({ id, motivo }: { id: string; motivo: string | null }) =>
  chamarApi(`/operadora/pedidos/${id}/estorno`, enviarJson('POST', { motivo })))

/** Os fornecedores de logística ativos, para escolher a transportadora. */
export const useFornecedoresDeLogistica = () =>
  useQuery({
    queryKey: ['operadora', 'fornecedores', 'logistica'],
    queryFn: () => chamarApi<Pagina<Fornecedor>>('/operadora/fornecedores?funcao=Logistica&ativos=true&tamanho=100'),
  })

export const useCadastroDoRevendedor = () => useGravar(({ id, nome, cnpj }: { id: string; nome: string; cnpj: string }) =>
  chamarApi(`/operadora/revendedores/${id}/cadastro`, enviarJson('PUT', { nome, cnpj })))

/** Mais cadastros de destino de QR e de links para hoje, além do limite diário da revenda. */
export const useLiberarDestinos = () => useGravar(({ id, quantidade }: { id: string; quantidade: number }) =>
  chamarApi(`/operadora/revendedores/${id}/liberacao-de-destinos`, enviarJson('POST', { quantidade })))

export const useSaldosAAcertar = (soComSaldo: boolean) =>
  useListaPaginada<SaldoAAcertar>(['operadora', 'financeiro', 'a-acertar', soComSaldo], '/operadora/financeiro/a-acertar', { filtros: { soComSaldo } })

export const useAcertosDaRevenda = (revendedorId: string) =>
  useQuery({
    queryKey: ['operadora', 'financeiro', 'acertos', revendedorId],
    queryFn: () => chamarApi<Acerto[]>(`/operadora/financeiro/acertos?revendedorId=${revendedorId}`),
  })

export const useRegistrarAcerto = () => useGravar((a: { revendedorId: string; valorCents: number; data: string; observacao: string | null }) =>
  chamarApi('/operadora/financeiro/acertos', enviarJson('POST', a)))

export const useExcluirAcerto = () => useGravar((id: string) => chamarApi(`/operadora/financeiro/acertos/${id}`, { method: 'DELETE' }))
