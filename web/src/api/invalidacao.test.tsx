import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, renderHook, waitFor } from '@testing-library/react'
import type { ReactNode } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { useVenderItens, useRenovar } from '@/balcao/consultas'
import {
  useAtivarLote, useCancelarVenda, useCorrigirVenda, useCortesia, useDesfazerRecebimento, useDesfazerRepasse, useReceberPorFora, useRepassar, useTrocarPlaca,
} from '@/painel/consultas'

afterEach(() => {
  vi.unstubAllGlobals()
})

/** Roda a gravação com a API respondendo 200 e devolve as chaves de consulta que ela mandou refazer. */
async function chavesRefeitas<R extends { mutateAsync: (v: never) => Promise<unknown> }>(usar: () => R, variaveis: Parameters<R['mutateAsync']>[0]) {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', { status: 200 })))
  const cliente = new QueryClient({ defaultOptions: { mutations: { retry: false } } })
  const invalidar = vi.spyOn(cliente, 'invalidateQueries')
  const { result } = renderHook(usar, { wrapper: ({ children }: { children: ReactNode }) => <QueryClientProvider client={cliente}>{children}</QueryClientProvider> })
  await act(() => result.current.mutateAsync(variaveis as never))
  await waitFor(() => expect(invalidar).toHaveBeenCalled())
  return invalidar.mock.calls.map(([filtro]) => String(filtro?.queryKey?.[0]))
}

/** O que muda quando uma venda muda de estado: lista, placa (e a ficha), comissão, razão e o resultado do mês. */
const DA_VENDA = ['vendas', 'placas', 'placa', 'comissoes', 'razao', 'financeiro']

describe('gravações que mudam outras telas refazem o que depende delas', () => {
  const venda = '00000000-0000-0000-0000-000000000001'

  it.each([
    ['receber por fora', () => chavesRefeitas(useReceberPorFora, { vendaId: venda, meio: 'Dinheiro' })],
    ['desfazer recebimento', () => chavesRefeitas(useDesfazerRecebimento, venda)],
    ['cancelar venda', () => chavesRefeitas(useCancelarVenda, { vendaId: venda, motivo: 'ClienteDesistiu', placa: 'Devolvida', observacao: null } as never)],
    ['cortesia', () => chavesRefeitas(useCortesia, venda)],
    ['trocar placa', () => chavesRefeitas(useTrocarPlaca, { vendaId: venda, codigo: 'K7M2PX', motivo: 'Danificada' } as never)],
    ['corrigir venda', () => chavesRefeitas(useCorrigirVenda, { vendaId: venda, valorCents: 100, vendedorId: null })],
  ])('%s', async (_nome, rodar) => {
    expect(await rodar()).toEqual(expect.arrayContaining(DA_VENDA))
  })

  it('registrar a venda cria o cliente e entra no vendido do mês', async () => {
    const chaves = await chavesRefeitas(useVenderItens, { venda: { itens: [] }, chave: 'k' } as never)
    expect(chaves).toEqual(expect.arrayContaining(['vendas', 'placas', 'placa', 'servicos', 'clientes', 'financeiro']))
  })

  it('renovação recebida por fora mexe no razão, no resultado, na comissão e na venda', async () => {
    const chaves = await chavesRefeitas(() => useRenovar('K7M2PX'), 'Dinheiro' as never)
    expect(chaves).toEqual(expect.arrayContaining(['placa', 'razao', 'financeiro', 'comissoes', 'vendas']))
  })

  it('repasse de comissão sai do caixa: refaz o financeiro, e desfazer também', async () => {
    expect(await chavesRefeitas(useRepassar, { vendedorId: venda, ate: null, comprovante: null })).toEqual(expect.arrayContaining(['comissoes', 'repasses', 'razao', 'financeiro']))
    expect(await chavesRefeitas(useDesfazerRepasse, venda)).toEqual(expect.arrayContaining(['comissoes', 'repasses', 'razao', 'financeiro']))
  })

  it('ativar o lote refaz a lista de lotes da revenda', async () => {
    expect(await chavesRefeitas(useAtivarLote, '0002')).toEqual(expect.arrayContaining(['pedidos', 'placas', 'lotes']))
  })
})
