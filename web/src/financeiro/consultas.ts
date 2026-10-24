import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { chamarApi, enviarJson } from '@/api/cliente'
import type { CategoriaDeDespesa, Despesa, DreDoMes, MesDoFinanceiro, MesDoHistorico, NaturezaDaRetirada, Retirada, Socio } from '@/api/tipos'

const doMes = (competencia: string | undefined) => (competencia ? `?competencia=${competencia}` : '')

export const useDre = (competencia: string | undefined) =>
  useQuery({ queryKey: ['financeiro', 'dre', competencia], queryFn: () => chamarApi<DreDoMes>(`/financeiro/dre${doMes(competencia)}`) })
export const useHistorico = (ate: string | undefined) =>
  useQuery({ queryKey: ['financeiro', 'historico', ate], queryFn: () => chamarApi<MesDoHistorico[]>(`/financeiro/historico${ate ? `?ate=${ate}` : ''}`) })
export const useMeses = () => useQuery({ queryKey: ['financeiro', 'meses'], queryFn: () => chamarApi<MesDoFinanceiro[]>('/financeiro/meses') })
export const useDespesas = (competencia: string | undefined) =>
  useQuery({ queryKey: ['financeiro', 'despesas', competencia], queryFn: () => chamarApi<Despesa[]>(`/financeiro/despesas${doMes(competencia)}`) })
export const useSocios = () => useQuery({ queryKey: ['financeiro', 'socios'], queryFn: () => chamarApi<Socio[]>('/financeiro/socios') })
export const useRetiradas = (competencia: string | undefined) =>
  useQuery({ queryKey: ['financeiro', 'retiradas', competencia], queryFn: () => chamarApi<Retirada[]>(`/financeiro/retiradas${doMes(competencia)}`) })

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

export const useFecharMes = () => useGravar((competencia: string) => chamarApi('/financeiro/fechamentos', enviarJson('POST', { competencia })))
export const useReabrirMes = () =>
  useGravar(({ competencia, motivo }: { competencia: string; motivo: string }) =>
    chamarApi(`/financeiro/fechamentos/${competencia}/reabertura`, enviarJson('POST', { motivo })))
