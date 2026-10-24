import { importLibrary, setOptions } from '@googlemaps/js-api-loader'
import { useQuery } from '@tanstack/react-query'
import { useEffect, useMemo, useRef, useState } from 'react'
import { chamarApi } from '@/api/cliente'
import { Aviso, Cabecalho, EstadoDaConsulta, Rolagem } from '@/componentes/Tela'
import { Card, CardContent } from '@/components/ui/card'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'

type Lugar = { clienteId: string; nome: string; lugarNoGoogle: string; placas: number; placasNoAr: number; leituras30Dias: number }
type Configuracao = { chave: string | null; idDoMapa: string }

const CENTRO_DO_BRASIL = { lat: -15.78, lng: -47.93 }
const TODAS = 'Todas'
let configurado = false

/** A cidade do lugar, pelo que o próprio Google devolve: município (BR) ou localidade. */
function cidadeDe(componentes: google.maps.places.AddressComponent[] | null | undefined) {
  const achado = componentes?.find((c) => c.types.includes('administrative_area_level_2')) ?? componentes?.find((c) => c.types.includes('locality'))
  return achado?.longText ?? null
}

/**
 * Os lugares vendidos no mapa do Google. A posição sai do próprio Places no
 * navegador, pelo id do lugar: dado do Places só pode aparecer em mapa do
 * Google e não é guardado na plataforma. Sem chave do mapa, fica a lista.
 */
export function TelaDoMapa() {
  const lugares = useQuery({ queryKey: ['mapa'], queryFn: () => chamarApi<Lugar[]>('/mapa') })
  const configuracao = useQuery({ queryKey: ['mapa', 'configuracao'], queryFn: () => chamarApi<Configuracao>('/mapa/configuracao'), staleTime: Infinity })
  const chave = configuracao.data?.chave
  const [cidades, setCidades] = useState<Record<string, string>>({})
  const [escolhida, setEscolhida] = useState<string | null>(null)

  // Uma aba por cidade, da que tem mais lugares para a que tem menos; o mapa abre na primeira.
  const abas = useMemo(() => {
    const contagem = new Map<string, number>()
    for (const cidade of Object.values(cidades)) contagem.set(cidade, (contagem.get(cidade) ?? 0) + 1)
    return [...contagem.entries()].sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0], 'pt-BR')).map(([cidade]) => cidade)
  }, [cidades])
  const cidade = escolhida ?? abas[0] ?? TODAS
  const visiveis = lugares.data?.filter((l) => cidade === TODAS || cidades[l.clienteId] === cidade)

  return (
    <>
      <Cabecalho titulo="Mapa" />
      {configuracao.data && !chave && <Aviso>Mapa indisponível.</Aviso>}
      {abas.length > 1 && (
        <Tabs value={cidade} onValueChange={setEscolhida} className="mt-3">
          <TabsList>
            {[...abas, TODAS].map((a) => <TabsTrigger key={a} value={a}>{a}</TabsTrigger>)}
          </TabsList>
        </Tabs>
      )}
      {chave && lugares.data && (
        <MapaDoGoogle chave={chave} idDoMapa={configuracao.data!.idDoMapa} lugares={lugares.data} cidade={cidade} aoAcharCidades={setCidades} />
      )}
      <Card className="mt-4">
        <CardContent>
          <EstadoDaConsulta carregando={lugares.isPending} erro={lugares.error} vazio={lugares.data?.length === 0} textoVazio="Nenhum lugar no mapa." />
          {visiveis && visiveis.length > 0 && (
            <Rolagem>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Negócio</TableHead>
                    <TableHead className="text-right">Placas</TableHead>
                    <TableHead className="text-right">No ar</TableHead>
                    <TableHead className="text-right">Leituras em 30 dias</TableHead>
                    <TableHead><span className="sr-only">Abrir</span></TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {visiveis.map((l) => (
                    <TableRow key={l.clienteId}>
                      <TableCell className="font-medium">{l.nome}</TableCell>
                      <TableCell className="text-right tabular-nums">{l.placas}</TableCell>
                      <TableCell className="text-right tabular-nums">{l.placasNoAr}</TableCell>
                      <TableCell className="text-right tabular-nums">{l.leituras30Dias}</TableCell>
                      <TableCell className="text-right">
                        <a className="text-sm underline underline-offset-4" target="_blank" rel="noreferrer noopener" href={`https://www.google.com/maps/place/?q=place_id:${encodeURIComponent(l.lugarNoGoogle)}`}>
                          Abrir no Google Maps
                        </a>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </Rolagem>
          )}
        </CardContent>
      </Card>
    </>
  )
}

type Props = {
  chave: string
  idDoMapa: string
  lugares: Lugar[]
  cidade: string
  aoAcharCidades: (cidades: Record<string, string>) => void
}

function MapaDoGoogle({ chave, idDoMapa, lugares, cidade, aoAcharCidades }: Props) {
  const elemento = useRef<HTMLDivElement>(null)
  const mapa = useRef<google.maps.Map | null>(null)
  const posicoes = useRef<{ clienteId: string; cidade: string | null; posicao: google.maps.LatLng }[]>([])
  const [pronto, setPronto] = useState(false)
  const [erro, setErro] = useState<string | null>(null)

  useEffect(() => {
    let cancelado = false
    async function desenhar() {
      if (!configurado) {
        setOptions({ key: chave, v: 'weekly', language: 'pt-BR', region: 'BR' })
        configurado = true
      }

      const [{ Map }, { Place }, { AdvancedMarkerElement }] = await Promise.all([
        importLibrary('maps'), importLibrary('places'), importLibrary('marker'),
      ])
      if (cancelado || !elemento.current) return
      mapa.current = new Map(elemento.current, { center: CENTRO_DO_BRASIL, zoom: 4, mapId: idDoMapa })
      const achados = await Promise.all(lugares.map(async (l) => {
        try {
          const lugar = new Place({ id: l.lugarNoGoogle })
          await lugar.fetchFields({ fields: ['location', 'addressComponents'] })
          return lugar.location ? { l, posicao: lugar.location, cidade: cidadeDe(lugar.addressComponents) } : null
        } catch {
          // Lugar que o Google não acha mais (fechou, mudou de id) fica só na lista.
          return null
        }
      }))
      if (cancelado) return
      posicoes.current = []
      const cidades: Record<string, string> = {}
      for (const achado of achados) {
        if (!achado) continue
        new AdvancedMarkerElement({ map: mapa.current, position: achado.posicao, title: `${achado.l.nome}: ${achado.l.leituras30Dias} leituras em 30 dias` })
        posicoes.current.push({ clienteId: achado.l.clienteId, cidade: achado.cidade, posicao: achado.posicao })
        if (achado.cidade) cidades[achado.l.clienteId] = achado.cidade
      }

      aoAcharCidades(cidades)
      setPronto(true)
    }

    desenhar().catch(() => !cancelado && setErro('Não foi possível carregar o mapa.'))
    return () => {
      cancelado = true
    }
  }, [chave, idDoMapa, lugares, aoAcharCidades])

  // A aba escolhida enquadra só os lugares daquela cidade.
  useEffect(() => {
    if (!pronto || !mapa.current) return
    const naCidade = posicoes.current.filter((p) => cidade === TODAS || p.cidade === cidade)
    if (naCidade.length === 1) {
      // Um lugar só: o enquadramento iria ao zoom máximo, dentro do telhado.
      mapa.current.setCenter(naCidade[0].posicao)
      mapa.current.setZoom(15)
    } else if (naCidade.length > 1) {
      const limites = new google.maps.LatLngBounds()
      for (const p of naCidade) limites.extend(p.posicao)
      mapa.current.fitBounds(limites)
    }
  }, [pronto, cidade])

  return (
    <>
      {erro && <Aviso>{erro}</Aviso>}
      <div ref={elemento} className="mt-3 aspect-video w-full rounded-lg border bg-muted" role="region" aria-label="Mapa dos lugares vendidos" />
    </>
  )
}
