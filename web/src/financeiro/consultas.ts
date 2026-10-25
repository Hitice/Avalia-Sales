import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { chamarApi, enviarJson } from '@/api/cliente'
import { useListaPaginada } from '@/lib/paginacao'
import type { Caixa, CategoriaDeDespesa, Despesa, DreDoMes, MesDoFinanceiro, MesDoHistorico, NaturezaDaRetirada, Retirada, Socio } from '@/api/tipos'

const doMes = (competencia: string | undefined) => (competencia ? `?competencia=${competencia}` : '')

export const useDre = (competencia: string | undefined) =>
  useQuery({ queryKey: ['financeiro', 'dre', competencia], queryFn: () => chamarApi<DreDoMes>(`/financeiro/dre${doMes(competencia)}`) })
export const useHistorico = (ate: string | undefined) =>
  useQuery({ queryKey: ['financeiro', 'historico', ate], queryFn: () => chamarApi<MesDoHistorico[]>(`/financeiro/historico${ate ? `?ate=${ate}` : ''}`) })
export const useMeses = () => useQuery({ queryKey: ['financeiro', 'meses'], queryFn: () => chamarApi<MesDoFinanceiro[]>('/financeiro/meses') })
export const useDespesas = (competencia: string | undefined) =>
  useListaPaginada<Despesa>(['financeiro', 'despesas', competencia], '/financeiro/despesas', { filtros: { competencia } })
export const useSocios = () => useQuery({ queryKey: ['financeiro', 'socios'], queryFn: () => chamarApi<Socio[]>('/financeiro/socios') })
export const useRetiradas = (competencia: string | undefined) =>
  useListaPaginada<Retirada>(['financeiro', 'retiradas', competencia], '/financeiro/retiradas', { filtros: { competencia } })

/** Toda gravação daqui mexe no razão: refaz o financeiro inteiro e o razão. */
function useGravar<T, R = unknown>(fazer: (dados: T) => Promise<R>) {
  const cliente = useQueryClient()
  return useMutation({
    mutationFn: fazer,
    meta: { sucesso: 'Alteração salva.' },
    onSuccess: () => Promise.all(['financeiro', 'razao', 'auditoria'].map((chave) => cliente.invalidateQueries({ queryKey: [chave] }))),
  })
}

export type GravarDespesa = { data: string; descricao: string; categoria: CategoriaDeDespesa; valorCents: number }
export const useSalvarDespesa = () =>
  useGravar(({ id, ...d }: GravarDespesa & { id?: string }) =>
    chamarApi<Despesa>(id ? `/financeiro/despesas/${id}` : '/financeiro/despesas', enviarJson(id ? 'PUT' : 'POST', d)))
export const useExcluirDespesa = () => useGravar((id: string) => chamarApi(`/financeiro/despesas/${id}`, { method: 'DELETE' }))

export type GravarSocio = { nome: string; cpf: string | null }
export const useSalvarSocio = () =>
  useGravar(({ id, ...s }: GravarSocio & { id?: string }) => chamarApi<Socio>(id ? `/financeiro/socios/${id}` : '/financeiro/socios', enviarJson(id ? 'PUT' : 'POST', s)))
export const useExcluirSocio = () => useGravar((id: string) => chamarApi(`/financeiro/socios/${id}`, { method: 'DELETE' }))

export type GravarRetirada = { socioId: string; data: string; natureza: NaturezaDaRetirada; valorCents: number; observacao: string | null }
export const useSalvarRetirada = () =>
  useGravar(({ id, ...r }: GravarRetirada & { id?: string }) =>
    chamarApi<Retirada>(id ? `/financeiro/retiradas/${id}` : '/financeiro/retiradas', enviarJson(id ? 'PUT' : 'POST', r)))
export const useExcluirRetirada = () => useGravar((id: string) => chamarApi(`/financeiro/retiradas/${id}`, { method: 'DELETE' }))

export const useCaixa = () => useQuery({ queryKey: ['financeiro', 'caixa'], queryFn: () => chamarApi<Caixa>('/financeiro/caixa') })
export const useSalvarSaldoInicial = () =>
  useGravar((s: { competencia: string; valorCents: number }) => chamarApi<Caixa>('/financeiro/caixa', enviarJson('PUT', s)))
export const useExcluirSaldoInicial = () => useGravar(() => chamarApi('/financeiro/caixa', { method: 'DELETE' }))

export const useFecharMes = () => useGravar((competencia: string) => chamarApi('/financeiro/fechamentos', enviarJson('POST', { competencia })))
export const useReabrirMes = () =>
  useGravar(({ competencia, motivo }: { competencia: string; motivo: string }) =>
    chamarApi(`/financeiro/fechamentos/${competencia}/reabertura`, enviarJson('POST', { motivo })))
