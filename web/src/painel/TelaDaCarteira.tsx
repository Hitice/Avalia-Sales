import { MessageCircle } from 'lucide-react'
import { useState } from 'react'
import { Link } from 'react-router'
import { formatarData, formatarTelefone } from '@/api/rotulos'
import type { PlacaDaCarteira } from '@/api/tipos'
import { useSessao } from '@/acesso/sessao'
import { Cabecalho, EstadoDaConsulta, EtiquetaDoEstado, Rolagem } from '@/componentes/Tela'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'
import { useCarteira } from './consultas'

const DIA = 86_400_000
type Filtro = 'todas' | 'vencendo' | 'vencidas'

/** O link do WhatsApp com a mensagem da renovação pronta; o telefone sem o 55 ganha o código do Brasil. */
function whatsapp(p: PlacaDaCarteira) {
  const digitos = p.telefone.replace(/\D/g, '')
  const numero = digitos.length <= 11 ? `55${digitos}` : digitos
  const quando = p.venceEm ? `vence em ${formatarData(p.venceEm)}` : 'está para vencer'
  const texto = `Olá, ${p.responsavel}! A placa ${p.codigo} da ${p.cliente} ${quando}. Teve ${p.leiturasEm30Dias} leituras nos últimos 30 dias. Quer renovar por mais um ano?`
  return `https://wa.me/${numero}?text=${encodeURIComponent(texto)}`
}

/**
 * A carteira: as placas vendidas, da que vence primeiro à última, com as
 * leituras (o argumento da renovação) e o caminho para renovar ou chamar o
 * cliente. O vendedor vê as dele; o revendedor, todas.
 */
export function TelaDaCarteira() {
  const dono = useSessao((s) => s.perfil?.papel === 'Dono')
  const carteira = useCarteira()
  const [filtro, setFiltro] = useState<Filtro>('todas')
  const [agora] = useState(() => Date.now())
  const placas = (carteira.data ?? []).filter((p) => {
    if (filtro === 'vencidas') return p.estado === 'Vencida' || p.estado === 'Carencia'
    if (filtro === 'vencendo') return p.estado === 'Ativa' && !!p.venceEm && Date.parse(p.venceEm) - agora <= 30 * DIA
    return true
  })

  return (
    <>
      <Cabecalho titulo={dono ? 'Carteira' : 'Minha carteira'}>
        <ToggleGroup aria-label="Quais placas" value={filtro} onValueChange={(v) => setFiltro(v as Filtro)}>
          <ToggleGroupItem value="todas">Todas</ToggleGroupItem>
          <ToggleGroupItem value="vencendo">Vencem em 30 dias</ToggleGroupItem>
          <ToggleGroupItem value="vencidas">Vencidas</ToggleGroupItem>
        </ToggleGroup>
      </Cabecalho>
      <Card>
        <CardContent>
          <EstadoDaConsulta carregando={carteira.isPending} erro={carteira.error} vazio={placas.length === 0}
            textoVazio={filtro === 'todas' ? 'Nenhuma placa vendida ainda.' : 'Nenhuma placa neste filtro.'} />
          {placas.length > 0 && (
            <Rolagem>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Cliente</TableHead>
                    <TableHead>Placa</TableHead>
                    <TableHead>Situação</TableHead>
                    <TableHead>Vence em</TableHead>
                    <TableHead className="text-right">Leituras em 30 dias</TableHead>
                    <TableHead><span className="sr-only">Ações</span></TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {placas.map((p) => (
                    <TableRow key={p.vendaId}>
                      <TableCell className="min-w-40 whitespace-normal">
                        <p className="font-medium">{p.cliente}</p>
                        <p className="text-xs font-normal text-muted-foreground">{p.responsavel}, {formatarTelefone(p.telefone)}</p>
                      </TableCell>
                      <TableCell><Link className="tabular-nums underline-offset-4 hover:underline" to={`/placa/${p.codigo}`}>{p.codigo}</Link></TableCell>
                      <TableCell><EtiquetaDoEstado estado={p.estado} largura /></TableCell>
                      <TableCell>{formatarData(p.venceEm)}</TableCell>
                      <TableCell className="text-right tabular-nums">{p.leiturasEm30Dias} <span className="text-xs text-muted-foreground">de {p.totalLeituras}</span></TableCell>
                      <TableCell>
                        <div className="flex items-center justify-end gap-2">
                          <Button asChild variant="outline"><Link to={`/placa/${p.codigo}`}>Renovar</Link></Button>
                          <Button asChild variant="ghost"><a href={whatsapp(p)} target="_blank" rel="noreferrer noopener"><MessageCircle aria-hidden />WhatsApp</a></Button>
                        </div>
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
