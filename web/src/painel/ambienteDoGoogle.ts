import { importLibrary, setOptions } from '@googlemaps/js-api-loader'
import type { Ambiente } from './mapaDeLugares'

const CENTRO_DO_BRASIL = { lat: -15.78, lng: -47.93 }
let configurado = false

/** A cidade do lugar, pelo que o próprio Google devolve: município (BR) ou localidade. */
function cidadeDe(componentes: google.maps.places.AddressComponent[] | null | undefined) {
  const achado = componentes?.find((c) => c.types.includes('administrative_area_level_2')) ?? componentes?.find((c) => c.types.includes('locality'))
  return achado?.longText ?? null
}

/** O Maps, o Places e os marcadores do Google, atrás de `Ambiente`. Precisa da chave do mapa. */
export async function carregarAmbienteDoGoogle(chave: string, idDoMapa: string): Promise<Ambiente> {
  if (!configurado) {
    setOptions({ key: chave, v: 'weekly', language: 'pt-BR', region: 'BR' })
    configurado = true
  }

  const [{ Map: MapaDoGoogleMaps }, { Place }, { AdvancedMarkerElement }] = await Promise.all([
    importLibrary('maps'), importLibrary('places'), importLibrary('marker'),
  ])
  return {
    // Tema escuro do Google: o mapa fica de fundo e os marcadores das vendas aparecem.
    novoMapa: (elemento) => new MapaDoGoogleMaps(elemento, { center: CENTRO_DO_BRASIL, zoom: 4, mapId: idDoMapa, colorScheme: 'DARK' }),
    async buscarLugar(id) {
      const lugar = new Place({ id })
      await lugar.fetchFields({ fields: ['location', 'addressComponents'] })
      return lugar.location ? { posicao: { lat: lugar.location.lat(), lng: lugar.location.lng() }, cidade: cidadeDe(lugar.addressComponents) } : null
    },
    novoMarcador(mapa, posicao, titulo) {
      const marcador = new AdvancedMarkerElement({ map: mapa as google.maps.Map, position: posicao, title: titulo })
      return { remover: () => { marcador.map = null } }
    },
    quandoOcioso: (mapa, acao) => { google.maps.event.addListenerOnce(mapa as google.maps.Map, 'idle', acao) },
  }
}
