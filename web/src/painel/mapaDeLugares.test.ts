import { describe, expect, it, vi } from 'vitest'
import { criarMapaDeLugares, enquadramento, TODAS, ZOOM_MAXIMO, type Ambiente, type Lugar, type LugarAchado, type MapaDoAmbiente } from './mapaDeLugares'

const lugar = (id: string, leituras = 10): Lugar => ({ clienteId: `c-${id}`, nome: `Negócio ${id}`, lugarNoGoogle: `g-${id}`, placas: 1, placasNoAr: 1, leituras30Dias: leituras })

/** O Google de mentira: conta mapas criados, buscas ao Places e marcadores vivos. */
function simulado(lugares: Record<string, LugarAchado | 'falha'>) {
  const mapa = { setCenter: vi.fn(), setZoom: vi.fn(), getZoom: vi.fn<() => number | null | undefined>(() => 4), fitBounds: vi.fn() } satisfies MapaDoAmbiente
  const vivos = new Map<number, { posicao: { lat: number; lng: number }; titulo: string }>()
  let proximo = 0
  let ocioso: (() => void) | null = null
  const ambiente = {
    novoMapa: vi.fn(() => mapa),
    buscarLugar: vi.fn(async (id: string) => {
      const achado = lugares[id]
      if (achado === 'falha') throw new Error('NOT_FOUND')
      return achado ?? null
    }),
    novoMarcador: vi.fn((_mapa: MapaDoAmbiente, posicao: { lat: number; lng: number }, titulo: string) => {
      const id = proximo++
      vivos.set(id, { posicao, titulo })
      return { remover: () => { vivos.delete(id) } }
    }),
    quandoOcioso: vi.fn((_mapa: MapaDoAmbiente, acao: () => void) => { ocioso = acao }),
  } satisfies Ambiente
  return { ambiente, mapa, vivos, ficarOcioso: () => ocioso?.() }
}

const elemento = {} as HTMLElement
const em = (lat: number, lng: number, cidade: string | null = 'Uberlândia'): LugarAchado => ({ posicao: { lat, lng }, cidade })

describe('mapa dos lugares', () => {
  it('cria o mapa uma vez e busca cada lugar no Places uma vez só, mesmo com várias atualizações', async () => {
    const g = simulado({ 'g-1': em(-18.9, -48.2), 'g-2': em(-18.95, -48.25) })
    const mapa = criarMapaDeLugares(g.ambiente, elemento)

    await mapa.atualizar([lugar('1'), lugar('2')])
    await mapa.atualizar([lugar('1', 11), lugar('2')])
    await mapa.atualizar([lugar('1', 12), lugar('2')])

    expect(g.ambiente.novoMapa).toHaveBeenCalledTimes(1)
    expect(g.ambiente.buscarLugar).toHaveBeenCalledTimes(2)
  })

  it('atualiza só o marcador que mudou, tira o que saiu e põe o que chegou', async () => {
    const g = simulado({ 'g-1': em(-18.9, -48.2), 'g-2': em(-18.95, -48.25), 'g-3': em(-18.8, -48.1) })
    const mapa = criarMapaDeLugares(g.ambiente, elemento)

    await mapa.atualizar([lugar('1'), lugar('2')])
    expect(g.vivos.size).toBe(2)
    expect(g.ambiente.novoMarcador).toHaveBeenCalledTimes(2)

    // Igual: nada se recria. Leituras do 1 mudaram: só ele. O 2 saiu e o 3 entrou.
    await mapa.atualizar([lugar('1'), lugar('2')])
    expect(g.ambiente.novoMarcador).toHaveBeenCalledTimes(2)
    await mapa.atualizar([lugar('1', 99), lugar('3')])
    expect(g.ambiente.novoMarcador).toHaveBeenCalledTimes(4)
    expect([...g.vivos.values()].map((m) => m.titulo).sort()).toEqual(['Negócio 1: 99 leituras em 30 dias', 'Negócio 3: 10 leituras em 30 dias'])
    expect(g.ambiente.buscarLugar).toHaveBeenCalledTimes(3)
  })

  it('divide a mesma busca entre atualizações simultâneas e não repete a que falhou', async () => {
    const g = simulado({ 'g-1': em(-18.9, -48.2), 'g-sumiu': 'falha' })
    const mapa = criarMapaDeLugares(g.ambiente, elemento)

    const [primeira, segunda] = await Promise.all([mapa.atualizar([lugar('1'), lugar('sumiu')]), mapa.atualizar([lugar('1'), lugar('sumiu')])])
    await mapa.atualizar([lugar('1'), lugar('sumiu')])

    expect(g.ambiente.buscarLugar).toHaveBeenCalledTimes(2)
    // A atualização que perdeu a corrida não devolve nada; a última vale. O lugar que o Google não acha fica de fora.
    expect(primeira).toBeNull()
    expect(segunda).toEqual({ 'c-1': 'Uberlândia' })
    expect(g.vivos.size).toBe(1)
  })

  it('devolve a cidade de cada cliente e ignora lugar sem posição', async () => {
    const g = simulado({ 'g-1': em(-18.9, -48.2, 'Uberlândia'), 'g-2': em(-19.7, -47.9, 'Araxá'), 'g-3': em(0, 0, null) })
    const mapa = criarMapaDeLugares(g.ambiente, elemento)

    expect(await mapa.atualizar([lugar('1'), lugar('2'), lugar('3'), lugar('4')])).toEqual({ 'c-1': 'Uberlândia', 'c-2': 'Araxá' })
  })

  it('na desmontagem tira os marcadores, esquece as posições e descarta a atualização que ainda voava', async () => {
    const g = simulado({ 'g-1': em(-18.9, -48.2) })
    const mapa = criarMapaDeLugares(g.ambiente, elemento)
    await mapa.atualizar([lugar('1')])
    expect(g.vivos.size).toBe(1)

    const voando = mapa.atualizar([lugar('1', 20)])
    mapa.destruir()

    expect(await voando).toBeNull()
    expect(g.vivos.size).toBe(0)
    mapa.enquadrar(TODAS)
    expect(g.mapa.fitBounds).not.toHaveBeenCalled()
  })
})

describe('enquadramento', () => {
  it('um lugar só centraliza em zoom 15, sem ir ao telhado', async () => {
    const g = simulado({ 'g-1': em(-18.9, -48.2) })
    const mapa = criarMapaDeLugares(g.ambiente, elemento)
    await mapa.atualizar([lugar('1')])

    mapa.enquadrar(TODAS)

    expect(g.mapa.setCenter).toHaveBeenCalledWith({ lat: -18.9, lng: -48.2 })
    expect(g.mapa.setZoom).toHaveBeenCalledWith(15)
    expect(g.mapa.fitBounds).not.toHaveBeenCalled()
  })

  it('vários lugares enquadram a cidade escolhida e limitam o zoom a 16 quando ficam todos no mesmo prédio', async () => {
    const g = simulado({ 'g-1': em(-18.9, -48.2), 'g-2': em(-18.95, -48.25), 'g-3': em(-19.7, -47.9, 'Araxá') })
    const mapa = criarMapaDeLugares(g.ambiente, elemento)
    await mapa.atualizar([lugar('1'), lugar('2'), lugar('3')])

    mapa.enquadrar('Uberlândia')

    expect(g.mapa.fitBounds).toHaveBeenCalledWith({ south: -18.95, north: -18.9, west: -48.25, east: -48.2 }, 24)
    g.mapa.getZoom.mockReturnValue(19)
    g.ficarOcioso()
    expect(g.mapa.setZoom).toHaveBeenCalledWith(ZOOM_MAXIMO)
    g.mapa.setZoom.mockClear()
    g.mapa.getZoom.mockReturnValue(14)
    g.ficarOcioso()
    expect(g.mapa.setZoom).not.toHaveBeenCalled()
  })

  it('a partir de 8 lugares fica com os 80% do meio em cada eixo', () => {
    const posicoes = Array.from({ length: 10 }, (_, i) => ({ lat: i, lng: 100 + i }))
    // Corte de 1 em cada ponta: o lugar 0 e o 9 saem do enquadramento.
    expect(enquadramento(posicoes)).toEqual({ south: 1, north: 8, west: 101, east: 108 })
    expect(enquadramento(posicoes.slice(0, 7))).toEqual({ south: 0, north: 6, west: 100, east: 106 })
  })
})
