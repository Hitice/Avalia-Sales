import { importLibrary, setOptions } from '@googlemaps/js-api-loader'
import { useQuery } from '@tanstack/react-query'
import { useEffect, useRef, useState } from 'react'
import { chamarApi } from '@/api/cliente'
import { Aviso, Cabecalho, EstadoDaConsulta, Rolagem } from '@/componentes/Tela'
import { Card, CardContent } from '@/components/ui/card'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'

type Lugar = { clienteId: string; nome: string; lugarNoGoogle: string; placas: number; placasNoAr: number; leituras30Dias: number }
type Configuracao = { chave: string | null; idDoMapa: string }

const CENTRO_DO_BRASIL = { lat: -15.78, lng: -47.93 }
let configurado = false

/**
 * Os lugares vendidos no mapa do Google. A posição sai do próprio Places no
 * navegador, pelo id do lugar: dado do Places só pode aparecer em mapa do
 * Google e não é guardado na plataforma. Sem chave do mapa, fica a lista.
 */
export function TelaDoMapa() {
  const lugares = useQuery({ queryKey: ['mapa'], queryFn: () => chamarApi<Lugar[]>('/mapa') })
  const configuracao = useQuery({ queryKey: ['mapa', 'configuracao'], queryFn: () => chamarApi<Configuracao>('/mapa/configuracao'), staleTime: Infinity })
  const chave = configuracao.data?.chave

  return (
    <>
      <Cabecalho titulo="Mapa" />
      {configuracao.data && !chave && <Aviso>Mapa indisponível.</Aviso>}
      {chave && lugares.data && <MapaDoGoogle chave={chave} idDoMapa={configuracao.data!.idDoMapa} lugares={lugares.data} />}
      <Card className="mt-4">
        <CardContent>
          <EstadoDaConsulta carregando={lugares.isPending} erro={lugares.error} vazio={lugares.data?.length === 0} textoVazio="Nenhum lugar no mapa." />
          {lugares.data && lugares.data.length > 0 && (
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
                  {lugares.data.map((l) => (
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

function MapaDoGoogle({ chave, idDoMapa, lugares }: { chave: string; idDoMapa: string; lugares: Lugar[] }) {
  const elemento = useRef<HTMLDivElement>(null)
  const [erro, setErro] = useState<string | null>(null)

  useEffect(() => {
    let cancelado = false
    async function desenhar() {
      if (!configurado) {
        setOptions({ key: chave, v: 'weekly', language: 'pt-BR', region: 'BR' })
        configurado = true
      }

      const [{ Map }, { Place }, { AdvancedMarkerElement }, { LatLngBounds }] = await Promise.all([
        importLibrary('maps'), importLibrary('places'), importLibrary('marker'), importLibrary('core'),
      ])
      if (cancelado || !elemento.current) return
      const mapa = new Map(elemento.current, { center: CENTRO_DO_BRASIL, zoom: 4, mapId: idDoMapa })
      const limites = new LatLngBounds()
      const achados = await Promise.all(lugares.map(async (l) => {
        try {
          const lugar = new Place({ id: l.lugarNoGoogle })
          await lugar.fetchFields({ fields: ['location'] })
          return lugar.location ? { l, posicao: lugar.location } : null
        } catch {
          // Lugar que o Google não acha mais (fechou, mudou de id) fica só na lista.
          return null
        }
      }))
      for (const achado of achados) {
        if (!achado) continue
        new AdvancedMarkerElement({ map: mapa, position: achado.posicao, title: `${achado.l.nome}: ${achado.l.leituras30Dias} leituras em 30 dias` })
        limites.extend(achado.posicao)
      }

      if (!limites.isEmpty()) mapa.fitBounds(limites)
    }

    desenhar().catch(() => !cancelado && setErro('Não foi possível carregar o mapa.'))
    return () => {
      cancelado = true
    }
  }, [chave, idDoMapa, lugares])

  return (
    <>
      {erro && <Aviso>{erro}</Aviso>}
      <div ref={elemento} className="mt-3 aspect-video w-full rounded-lg border bg-muted" role="region" aria-label="Mapa dos lugares vendidos" />
    </>
  )
}
