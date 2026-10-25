/**
 * A lógica do mapa dos lugares vendidos, sem tocar no Google: tudo o que vem do
 * Maps e do Places entra por `Ambiente`, e o teste põe um simulado no lugar.
 */

export type Lugar = { clienteId: string; nome: string; lugarNoGoogle: string; placas: number; placasNoAr: number; leituras30Dias: number }
export type Posicao = { lat: number; lng: number }
export type Limites = { south: number; west: number; north: number; east: number }
export type LugarAchado = { posicao: Posicao; cidade: string | null }

export type MapaDoAmbiente = {
  setCenter(posicao: Posicao): void
  setZoom(zoom: number): void
  getZoom(): number | null | undefined
  fitBounds(limites: Limites, margem: number): void
}

export type Ambiente = {
  novoMapa(elemento: HTMLElement): MapaDoAmbiente
  /** Uma chamada ao Places por lugar, cobrada: quem chama guarda o resultado. Lança se o Google não achar. */
  buscarLugar(id: string): Promise<LugarAchado | null>
  novoMarcador(mapa: MapaDoAmbiente, posicao: Posicao, titulo: string): { remover(): void }
  quandoOcioso(mapa: MapaDoAmbiente, acao: () => void): void
}

export const TODAS = 'Todas'
// Perto disso o mapa mostra o telhado, não a rua.
export const ZOOM_MAXIMO = 16
const ZOOM_DE_UM_LUGAR = 15
const MARGEM = 24

/**
 * O enquadramento dos lugares. A partir de 8, fica com os 80% do meio em cada
 * eixo: um lugar isolado na ponta da cidade abriria o mapa demais e juntaria
 * os alfinetes do centro. Quem fica de fora aparece ao arrastar.
 */
export function enquadramento(posicoes: Posicao[]): Limites {
  const corte = posicoes.length >= 8 ? Math.floor(posicoes.length * 0.1) : 0
  const meio = (valores: number[]) => valores.sort((a, b) => a - b).slice(corte, valores.length - corte)
  const latitudes = meio(posicoes.map((p) => p.lat))
  const longitudes = meio(posicoes.map((p) => p.lng))
  return { south: latitudes[0], north: latitudes[latitudes.length - 1], west: longitudes[0], east: longitudes[longitudes.length - 1] }
}

export const tituloDoMarcador = (l: Lugar) => `${l.nome}: ${l.leituras30Dias} leituras em 30 dias`

type NoMapa = { chave: string; remover(): void }

export type MapaDeLugares = {
  /** Põe os lugares no mapa que já existe: marcador novo só para o que mudou. Devolve a cidade de cada cliente, ou nada se outra atualização passou na frente. */
  atualizar(lugares: Lugar[]): Promise<Record<string, string> | null>
  /** Enquadra os lugares da cidade (ou todos, com `TODAS`). */
  enquadrar(cidade: string): void
  /** Tira os marcadores e esquece as posições. */
  destruir(): void
}

/**
 * Cria o mapa uma vez. O Places é cobrado por chamada, então a posição de cada
 * `lugarNoGoogle` é buscada uma vez só (inclusive a que falhou, que fica fora
 * até a tela ser aberta de novo) e as chamadas simultâneas dividem a mesma busca.
 */
export function criarMapaDeLugares(ambiente: Ambiente, elemento: HTMLElement): MapaDeLugares {
  const mapa = ambiente.novoMapa(elemento)
  const posicoesPorLugar = new Map<string, Promise<LugarAchado | null>>()
  const marcadores = new Map<string, NoMapa>()
  let achados: { clienteId: string; cidade: string | null; posicao: Posicao }[] = []
  let geracao = 0
  let destruido = false

  const posicaoDe = (id: string) => {
    let busca = posicoesPorLugar.get(id)
    if (!busca) {
      // Lugar que o Google não acha mais (fechou, mudou de id) fica só na lista.
      busca = ambiente.buscarLugar(id).catch(() => null)
      posicoesPorLugar.set(id, busca)
    }
    return busca
  }

  return {
    async atualizar(lugares) {
      const minha = ++geracao
      const buscados = await Promise.all(lugares.map(async (l) => ({ l, achado: await posicaoDe(l.lugarNoGoogle) })))
      if (destruido || minha !== geracao) return null

      const cidades: Record<string, string> = {}
      const vivos = new Set<string>()
      achados = []
      for (const { l, achado } of buscados) {
        if (!achado) continue
        vivos.add(l.clienteId)
        achados.push({ clienteId: l.clienteId, cidade: achado.cidade, posicao: achado.posicao })
        if (achado.cidade) cidades[l.clienteId] = achado.cidade
        const titulo = tituloDoMarcador(l)
        const chave = `${achado.posicao.lat},${achado.posicao.lng}|${titulo}`
        const atual = marcadores.get(l.clienteId)
        if (atual?.chave === chave) continue
        atual?.remover()
        const marcador = ambiente.novoMarcador(mapa, achado.posicao, titulo)
        marcadores.set(l.clienteId, { chave, remover: () => marcador.remover() })
      }

      for (const [clienteId, marcador] of marcadores) {
        if (vivos.has(clienteId)) continue
        marcador.remover()
        marcadores.delete(clienteId)
      }

      return cidades
    },

    enquadrar(cidade) {
      const naCidade = achados.filter((p) => cidade === TODAS || p.cidade === cidade)
      if (naCidade.length === 1) {
        // Um lugar só: o enquadramento iria ao zoom máximo, dentro do telhado.
        mapa.setCenter(naCidade[0].posicao)
        mapa.setZoom(ZOOM_DE_UM_LUGAR)
      } else if (naCidade.length > 1) {
        mapa.fitBounds(enquadramento(naCidade.map((p) => p.posicao)), MARGEM)
        // Vários lugares no mesmo prédio dariam um enquadramento sem área.
        ambiente.quandoOcioso(mapa, () => {
          if (!destruido && (mapa.getZoom() ?? 0) > ZOOM_MAXIMO) mapa.setZoom(ZOOM_MAXIMO)
        })
      }
    },

    destruir() {
      destruido = true
      for (const marcador of marcadores.values()) marcador.remover()
      marcadores.clear()
      posicoesPorLugar.clear()
      achados = []
    },
  }
}
