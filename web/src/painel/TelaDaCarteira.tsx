import { useState } from 'react'
import { Link } from 'react-router'
import { formatarData, formatarTelefone, telefoneMascarado } from '@/api/rotulos'
import type { PlacaDaCarteira } from '@/api/tipos'
import { useSessao } from '@/acesso/sessao'
import { AcoesDaLinha } from '@/componentes/AcoesDaLinha'
import { ListaPaginada } from '@/componentes/ListaPaginada'
import { Cabecalho, EtiquetaDoEstado, Rolagem } from '@/componentes/Tela'
import { Card, CardContent } from '@/components/ui/card'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'
import { useCarteira } from './consultas'

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
  const [filtro, setFiltro] = useState<Filtro>('todas')
  const carteira = useCarteira(filtro === 'vencendo' ? 'Vencendo' : filtro === 'vencidas' ? 'Vencidas' : undefined)

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
          <ListaPaginada lista={carteira} nomeDaBusca="Buscar na carteira" placeholder="Cliente ou placa"
            textoVazio={filtro === 'todas' ? 'Nenhuma placa vendida ainda.' : 'Nenhuma placa neste filtro.'}>
            {(placas) => (
              <Rolagem>
                <Table semBusca>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Cliente</TableHead>
                      <TableHead>Placa</TableHead>
                      <TableHead tipo="situacao">Situação</TableHead>
                      <TableHead tipo="data">Vence em</TableHead>
                      <TableHead tipo="numero">Leituras em 30 dias</TableHead>
                      <TableHead tipo="acoes" />
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
                        <TableCell><EtiquetaDoEstado estado={p.estado} /></TableCell>
                        <TableCell>{formatarData(p.venceEm)}</TableCell>
                        <TableCell>{p.leiturasEm30Dias} <span className="text-xs text-muted-foreground">de {p.totalLeituras}</span></TableCell>
                        <TableCell>
                          <AcoesDaLinha nome={`placa ${p.codigo}`} principal={{ rotulo: 'Renovar', para: `/placa/${p.codigo}` }}
                            outras={telefoneMascarado(p.telefone) ? [] : [{ rotulo: 'WhatsApp', href: whatsapp(p) }]} />
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </Rolagem>
            )}
          </ListaPaginada>
        </CardContent>
      </Card>
    </>
  )
}
