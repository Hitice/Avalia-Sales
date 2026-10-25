import { useQuery } from '@tanstack/react-query'
import { useEffect, useMemo, useRef, useState } from 'react'
import { chamarApi } from '@/api/cliente'
import { AcoesDaLinha } from '@/componentes/AcoesDaLinha'
import { Aviso, Cabecalho, EstadoDaConsulta, Rolagem } from '@/componentes/Tela'
import { Card, CardContent } from '@/components/ui/card'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { carregarAmbienteDoGoogle } from './ambienteDoGoogle'
import { criarMapaDeLugares, TODAS, type Lugar, type MapaDeLugares } from './mapaDeLugares'

type Configuracao = { chave: string | null; idDoMapa: string }

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
                    <TableHead tipo="numero">Placas</TableHead>
                    <TableHead tipo="numero">No ar</TableHead>
                    <TableHead tipo="numero">Leituras em 30 dias</TableHead>
                    <TableHead tipo="acoes" />
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {visiveis.map((l) => (
                    <TableRow key={l.clienteId}>
                      <TableCell className="font-medium">{l.nome}</TableCell>
                      <TableCell>{l.placas}</TableCell>
                      <TableCell>{l.placasNoAr}</TableCell>
                      <TableCell>{l.leituras30Dias}</TableCell>
                      <TableCell>
                        <AcoesDaLinha nome={l.nome}
                          principal={{ rotulo: 'Abrir no Google Maps', href: `https://www.google.com/maps/place/?q=place_id:${encodeURIComponent(l.lugarNoGoogle)}` }} />
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

/**
 * O mapa nasce uma vez por chave. Lugar novo ou alterado só mexe nos
 * marcadores, e a posição de cada lugar do Google é buscada uma vez só.
 */
function MapaDoGoogle({ chave, idDoMapa, lugares, cidade, aoAcharCidades }: Props) {
  const elemento = useRef<HTMLDivElement>(null)
  const [mapa, setMapa] = useState<MapaDeLugares | null>(null)
  const [pronto, setPronto] = useState(false)
  const [erro, setErro] = useState<string | null>(null)

  useEffect(() => {
    let cancelado = false
    let criado: MapaDeLugares | null = null
    carregarAmbienteDoGoogle(chave, idDoMapa)
      .then((ambiente) => {
        if (cancelado || !elemento.current) return
        criado = criarMapaDeLugares(ambiente, elemento.current)
        setMapa(criado)
      })
      .catch(() => !cancelado && setErro('Não foi possível carregar o mapa.'))
    return () => {
      cancelado = true
      criado?.destruir()
      setMapa(null)
      setPronto(false)
    }
  }, [chave, idDoMapa])

  useEffect(() => {
    if (!mapa) return
    let cancelado = false
    mapa.atualizar(lugares)
      .then((cidades) => {
        if (cancelado || !cidades) return
        aoAcharCidades(cidades)
        setPronto(true)
      })
      .catch(() => !cancelado && setErro('Não foi possível carregar o mapa.'))
    return () => { cancelado = true }
  }, [mapa, lugares, aoAcharCidades])

  // A aba escolhida enquadra só os lugares daquela cidade.
  useEffect(() => {
    if (pronto) mapa?.enquadrar(cidade)
  }, [mapa, pronto, cidade])

  return (
    <>
      {erro && <Aviso>{erro}</Aviso>}
      <div ref={elemento} className="mt-3 aspect-video w-full rounded-lg border bg-muted" role="region" aria-label="Mapa dos lugares vendidos" />
    </>
  )
}
