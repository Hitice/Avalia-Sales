import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { useSessao } from '@/acesso/sessao'
import type { ResultadoDoGrupo } from '@/api/tipos'
import { TelaDoResultadoDoGrupo } from './TelaDoResultadoDoGrupo'

const linha = (chave: string, rotulo: string, tipo: ResultadoDoGrupo['linhas'][number]['tipo'], revenda: number, operadora: number, eliminacoes: number) =>
  ({ chave, rotulo, tipo, revendaCents: revenda, operadoraCents: operadora, eliminacoesCents: eliminacoes, consolidadoCents: revenda + operadora + eliminacoes })

const resultado: ResultadoDoGrupo = {
  competencia: '2026-10',
  fechado: false,
  fechadoEm: null,
  revendasDoGrupo: 1,
  linhas: [
    linha('receita-das-vendas', 'Receita das vendas ao cliente final', 'Receita', 124_900, 0, 0),
    linha('taxa-da-plataforma', 'Taxa da plataforma', 'Receita', 0, 59_600, -29_800),
    linha('total-da-receita', 'Receita', 'Total', 124_900, 59_600, -29_800),
    linha('taxa-da-plataforma-paga', 'Taxa da plataforma paga pela revenda', 'Custo', 29_800, 0, -29_800),
    linha('total-dos-custos', 'Custos e despesas', 'Total', 29_800, 0, -29_800),
    linha('resultado', 'Resultado', 'Resultado', 95_100, 59_600, 0),
  ],
  conciliacao: [
    { chave: 'taxa-da-plataforma', rotulo: 'Taxa da plataforma: paga pela revenda do grupo e recebida pela operadora', naRevendaCents: 29_800, naOperadoraCents: 29_800, diferencaCents: 0 },
  ],
  diferencaDaConciliacaoCents: 0,
  aindaNaoReconhecido: { totalCents: 1_500_000, pedidos: 2, daRevendaDoGrupoCents: 500_000 },
  pendencias: { lotesSemCusto: 1, placasVendidasSemCusto: 0 },
  retiradasDeSocioCents: 0,
}

afterEach(() => {
  vi.unstubAllGlobals()
  useSessao.getState().limpar()
})

function abrir(resposta: (url: string) => Response) {
  vi.stubGlobal('fetch', vi.fn(async (url: string) => resposta(url)))
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MemoryRouter><TelaDoResultadoDoGrupo /></MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('TelaDoResultadoDoGrupo', () => {
  it('mostra as quatro colunas, a conciliação em zero e o que ainda não foi reconhecido', async () => {
    abrir((url) => new Response(JSON.stringify(url.includes('/meses') ? [{ competencia: '2026-10', fechado: false, fechadoEm: null }] : resultado), { status: 200 }))

    expect(await screen.findByText('Receita das vendas ao cliente final')).toBeInTheDocument()
    for (const coluna of ['Revenda do grupo', 'Operadora', 'Eliminações', 'Consolidado']) {
      expect(screen.getAllByText(coluna).length).toBeGreaterThan(0)
    }

    const linhaDaTaxa = screen.getByText('Taxa da plataforma').closest('tr')!
    expect(within(linhaDaTaxa).getByText(/596,00/)).toBeInTheDocument()
    expect(within(linhaDaTaxa).getByText(/-R\$\s?298,00/)).toBeInTheDocument()
    expect(screen.getByText('Fecha em zero')).toBeInTheDocument()
    expect(screen.getByText(/2 pedidos pagos esperando ativação: R\$\s?15\.000,00/)).toBeInTheDocument()
    expect(screen.getByText(/1 lote despachado sem custo informado/)).toBeInTheDocument()
  })

  it('avisa quando nenhuma revenda foi marcada como do grupo', async () => {
    abrir((url) => new Response(JSON.stringify(url.includes('/meses') ? [] : { ...resultado, revendasDoGrupo: 0 }), { status: 200 }))

    expect(await screen.findByText(/Nenhuma revenda está marcada como do grupo/)).toBeInTheDocument()
  })

  it('mostra o erro da API no lugar do demonstrativo', async () => {
    abrir((url) => url.includes('/meses')
      ? new Response('[]', { status: 200 })
      : new Response(JSON.stringify({ title: 'Falhou', detail: 'Não foi possível ler o resultado.' }), { status: 500 }))

    await waitFor(() => expect(screen.getByRole('alert')).toBeInTheDocument())
    expect(screen.queryByText('Resultado do mês')).not.toBeInTheDocument()
  })

  it('mostra o estado de carregamento antes da resposta', () => {
    vi.stubGlobal('fetch', vi.fn(() => new Promise(() => {})))
    render(
      <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
        <MemoryRouter><TelaDoResultadoDoGrupo /></MemoryRouter>
      </QueryClientProvider>,
    )

    expect(screen.getAllByRole('status').length).toBeGreaterThan(0)
  })
})
