import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { chamarApi, enviarJson } from '@/api/cliente'
import type { CategoriaDeDespesaDaOperadora, CustoDoLote, DespesaDaOperadora, MesDoFinanceiro, ReceitaDaRevenda, ResultadoDoGrupo } from '@/api/tipos'
import { useListaPaginada } from '@/lib/paginacao'

const doMes = (competencia: string | undefined) => (competencia ? `?competencia=${competencia}` : '')

export const useResultadoDoGrupo = (competencia: string | undefined) =>
  useQuery({ queryKey: ['operadora', 'financeiro', 'resultado', competencia], queryFn: () => chamarApi<ResultadoDoGrupo>(`/operadora/financeiro/resultado${doMes(competencia)}`) })
export const useMesesDaOperadora = () =>
  useQuery({ queryKey: ['operadora', 'financeiro', 'meses'], queryFn: () => chamarApi<MesDoFinanceiro[]>('/operadora/financeiro/meses') })
export const useReceitaPorRevenda = (competencia: string | undefined) =>
  useListaPaginada<ReceitaDaRevenda>(['operadora', 'financeiro', 'receita-por-revenda', competencia], '/operadora/financeiro/receita-por-revenda', { filtros: { competencia } })
export const useDespesasDaOperadora = (competencia: string | undefined) =>
  useListaPaginada<DespesaDaOperadora>(['operadora', 'financeiro', 'despesas', competencia], '/operadora/financeiro/despesas', { filtros: { competencia } })
export const useCustosDeLote = (pendentes: boolean) =>
  useListaPaginada<CustoDoLote>(['operadora', 'financeiro', 'custos-de-lote', pendentes], '/operadora/financeiro/custos-de-lote', { filtros: { pendentes } })

/** Toda gravação daqui mexe no livro da operadora ou no custo dos lotes: refaz o resultado e as listas dela. */
function useGravar<T>(fazer: (dados: T) => Promise<unknown>) {
  const cliente = useQueryClient()
  return useMutation({ mutationFn: fazer, meta: { sucesso: 'Alteração salva.' }, onSuccess: () => cliente.invalidateQueries({ queryKey: ['operadora'] }) })
}

export type GravarDespesaDaOperadora = { data: string; descricao: string; categoria: CategoriaDeDespesaDaOperadora; valorCents: number }
export const useSalvarDespesaDaOperadora = () =>
  useGravar(({ id, ...d }: GravarDespesaDaOperadora & { id?: string }) =>
    chamarApi<DespesaDaOperadora>(id ? `/operadora/financeiro/despesas/${id}` : '/operadora/financeiro/despesas', enviarJson(id ? 'PUT' : 'POST', d)))
export const useExcluirDespesaDaOperadora = () => useGravar((id: string) => chamarApi(`/operadora/financeiro/despesas/${id}`, { method: 'DELETE' }))

export type GravarCusto = { graficaCents: number; acrilicoCents: number }
/** Sem custo ainda, cria; com custo, edita: a API recusa o contrário. */
export const useSalvarCustoDoLote = () =>
  useGravar(({ loteId, informado, ...c }: GravarCusto & { loteId: string; informado: boolean }) =>
    chamarApi<CustoDoLote>(informado ? `/operadora/financeiro/custos-de-lote/${loteId}` : '/operadora/financeiro/custos-de-lote', enviarJson(informado ? 'PUT' : 'POST', informado ? c : { loteId, ...c })))
export const useExcluirCustoDoLote = () => useGravar((loteId: string) => chamarApi(`/operadora/financeiro/custos-de-lote/${loteId}`, { method: 'DELETE' }))

export const useFecharMesDaOperadora = () => useGravar((competencia: string) => chamarApi('/operadora/financeiro/fechamentos', enviarJson('POST', { competencia })))
export const useReabrirMesDaOperadora = () =>
  useGravar(({ competencia, motivo }: { competencia: string; motivo: string }) =>
    chamarApi(`/operadora/financeiro/fechamentos/${competencia}/reabertura`, enviarJson('POST', { motivo })))
