import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { chamarApi, enviarJson } from '@/api/cliente'
import type { DestinoInformado, LoteParaAtivar, Lugar, PlacasAtivadas } from '@/api/tipos'

/** O código do lote como a API guarda: maiúsculo, sem separador, I e L viram 1 e O vira 0. */
export function normalizarCodigoDoLote(digitado: string): string {
  return digitado.trim().toUpperCase().replace(/[\s\-._]/g, '').replace(/[IL]/g, '1').replace(/O/g, '0').slice(0, 6)
}

export const codigoDoLoteCompleto = (codigo: string) => /^[0-9A-HJKMNP-TV-Z]{6}$/.test(codigo)

const chave = (codigo: string) => ['ativacao', codigo]

export function useLoteParaAtivar(codigo: string | null) {
  return useQuery({
    queryKey: chave(codigo ?? ''),
    queryFn: () => chamarApi<LoteParaAtivar>(`/ativacao/${codigo}`),
    enabled: !!codigo,
    retry: false,
  })
}

export const buscarLugaresDoLote = (codigo: string) => (texto: string) =>
  chamarApi<Lugar[]>(`/ativacao/${codigo}/lugares?texto=${encodeURIComponent(texto)}`)

export type Ativacao = { codigoDaPlaca: string | null; empresa: string; telefone: string; destino: DestinoInformado }

export function useAtivar(codigo: string) {
  const cliente = useQueryClient()
  return useMutation({
    mutationFn: (ativacao: Ativacao) => chamarApi<PlacasAtivadas>(`/ativacao/${codigo}`, enviarJson('POST', ativacao)),
    onSuccess: () => cliente.invalidateQueries({ queryKey: chave(codigo) }),
  })
}

export function useTrocarDestino(codigo: string) {
  const cliente = useQueryClient()
  return useMutation({
    mutationFn: (troca: { codigoDaPlaca: string | null; destino: DestinoInformado }) =>
      chamarApi<PlacasAtivadas>(`/ativacao/${codigo}/destino`, enviarJson('PUT', troca)),
    onSuccess: () => cliente.invalidateQueries({ queryKey: chave(codigo) }),
  })
}
