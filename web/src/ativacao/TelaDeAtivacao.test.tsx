import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { LoteParaAtivar, PlacasAtivadas } from '@/api/tipos'
import { codigoDoLoteCompleto, normalizarCodigoDoLote } from './consultas'
import { TelaDeAtivacao } from './TelaDeAtivacao'

function resposta(status: number, corpo: unknown = {}) {
  return new Response(JSON.stringify(corpo), { status, headers: { 'Content-Type': 'application/json' } })
}

const placa = (codigo: string, ativa = false, destino: string | null = null) =>
  ({ codigo, ativa, destino, endereco: `https://placas.exemplo.com.br/Q/${codigo}` })

const avaliacao: LoteParaAtivar = { rotulo: 'LT-0042', tipo: 'Avaliacao', placas: [placa('AAAAA1'), placa('AAAAA2', true, 'https://g.page/r/x/review')], placaUnica: false, todasAtivas: false }
const cardapio: LoteParaAtivar = { rotulo: 'LT-0043', tipo: 'Cardapio', placas: [placa('BBBBB1'), placa('BBBBB2')], placaUnica: false, todasAtivas: false }

/** A API falsa: o GET do lote devolve `lote`; o POST e o PUT devolvem as placas ativadas e guardam o corpo. */
function api(lote: LoteParaAtivar | null) {
  const enviados: { url: string; metodo: string; corpo: unknown }[] = []
  vi.stubGlobal('fetch', vi.fn((url: string, opcoes: RequestInit = {}) => {
    const metodo = opcoes.method ?? 'GET'
    if (metodo === 'GET') {
      return Promise.resolve(lote ? resposta(200, lote) : resposta(422, { detail: 'Código não encontrado. Confira o código que veio com as placas.' }))
    }

    const corpo = JSON.parse(opcoes.body as string) as unknown
    enviados.push({ url, metodo, corpo })
    const destino = (corpo as { destino: { link: string } }).destino.link
    const ativadas: PlacasAtivadas = { destino, placas: lote!.placas.map((p) => ({ ...p, ativa: true, destino })) }
    return Promise.resolve(resposta(200, ativadas))
  }))
  return enviados
}

function abrir(caminho = '/ativar') {
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MemoryRouter initialEntries={[caminho]}><TelaDeAtivacao /></MemoryRouter>
    </QueryClientProvider>,
  )
}

afterEach(() => vi.unstubAllGlobals())

describe('código do lote', () => {
  it('normaliza como a API: maiúsculo, sem separador, O vira 0 e I e L viram 1', () => {
    expect(normalizarCodigoDoLote(' k7m-2px ')).toBe('K7M2PX')
    expect(normalizarCodigoDoLote('oil')).toBe('011')
    expect(codigoDoLoteCompleto('K7M2PX')).toBe(true)
    expect(codigoDoLoteCompleto('K7M2PU')).toBe(false)
  })
})

describe('TelaDeAtivacao', () => {
  it('pede o código do lote e mostra a recusa da API', async () => {
    api(null)
    abrir()
    const campo = screen.getByLabelText('Código que veio com as placas')
    const continuar = screen.getByRole('button', { name: 'Continuar' })
    expect(continuar).toBeDisabled()
    fireEvent.change(campo, { target: { value: 'zzzzzz' } })
    fireEvent.click(continuar)
    expect(await screen.findByText('Código não encontrado. Confira o código que veio com as placas.')).toBeInTheDocument()
  })

  it('na avaliação escolhe a placa, cola o link do Google e chega ao NFC', async () => {
    const enviados = api(avaliacao)
    abrir('/ativar?codigo=k7m2px')

    fireEvent.click(await screen.findByRole('button', { name: 'AAAAA1' }))
    fireEvent.change(screen.getByLabelText('Nome da empresa'), { target: { value: 'Padaria Pão Quente' } })
    fireEvent.change(screen.getByLabelText('Telefone com DDD'), { target: { value: '34999991234' } })
    fireEvent.change(screen.getByLabelText('Ou o link de avaliação do perfil'), { target: { value: 'https://g.page/r/abc/review' } })
    fireEvent.click(screen.getByRole('button', { name: 'Ativar placa' }))

    expect(await screen.findByText('Placa ativada')).toBeInTheDocument()
    expect(enviados[0]).toMatchObject({
      url: '/api/ativacao/K7M2PX',
      metodo: 'POST',
      corpo: { codigoDaPlaca: 'AAAAA1', empresa: 'Padaria Pão Quente', destino: { tipo: 'Google', link: 'https://g.page/r/abc/review' } },
    })
    expect(screen.getAllByRole('button', { name: /NFC/ }).length).toBeGreaterThan(0)
  })

  it('placa já ativada abre a troca de destino em vez do cadastro', async () => {
    const enviados = api(avaliacao)
    abrir('/ativar?codigo=K7M2PX')

    fireEvent.click(await screen.findByRole('button', { name: 'AAAAA2 - ativa' }))
    expect(screen.queryByLabelText('Nome da empresa')).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Trocar destino' }))
    fireEvent.change(await screen.findByLabelText('Ou o link de avaliação do perfil'), { target: { value: 'https://g.page/r/novo/review' } })
    fireEvent.click(screen.getByRole('button', { name: 'Salvar destino' }))

    expect(await screen.findByText('Placa ativada')).toBeInTheDocument()
    expect(enviados[0]).toMatchObject({ url: '/api/ativacao/K7M2PX/destino', metodo: 'PUT', corpo: { codigoDaPlaca: 'AAAAA2' } })
  })

  it('no cardápio um link só ativa todas as placas, sem pedir o código da placa', async () => {
    const enviados = api(cardapio)
    abrir('/ativar?codigo=K7M2PX')

    await screen.findByText(/2 placas de cardápio/)
    expect(screen.queryByLabelText('Código impresso na placa')).not.toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('Nome da empresa'), { target: { value: 'Restaurante Sabor' } })
    fireEvent.change(screen.getByLabelText('Telefone com DDD'), { target: { value: '3433334444' } })
    fireEvent.change(screen.getByLabelText('Link do cardápio digital'), { target: { value: 'cardapio.sabor.com.br' } })
    fireEvent.click(screen.getByRole('button', { name: 'Ativar placas' }))

    expect(await screen.findByText('Placa BBBBB2')).toBeInTheDocument()
    expect(enviados[0]).toMatchObject({ metodo: 'POST', corpo: { codigoDaPlaca: null, destino: { tipo: 'Link', link: 'cardapio.sabor.com.br' } } })
  })
})
