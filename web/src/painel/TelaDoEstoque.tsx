import { useState } from 'react'
import { Link } from 'react-router'
import { mensagemDoErro } from '@/api/cliente'
import { formatarData, ROTULO_DO_ESTADO } from '@/api/rotulos'
import type { EstadoDaPlaca } from '@/api/tipos'
import { useSessao } from '@/acesso/sessao'
import { Cabecalho, Erro, EstadoDaConsulta, EtiquetaDoEstado, Rolagem } from '@/componentes/Tela'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Textarea } from '@/components/ui/textarea'
import { useConsignar, useDevolver, useEquipe, usePlacas } from './consultas'

const ESTADOS: EstadoDaPlaca[] = ['EmEstoque', 'Ativa', 'Carencia', 'Vencida', 'Suspensa']
const TODOS = 'todos'

/** O estoque do revendedor e a consignação ao vendedor. O vendedor vê só as dele. */
export function TelaDoEstoque() {
  const dono = useSessao((s) => s.perfil?.papel === 'Dono')
  const [estado, setEstado] = useState<EstadoDaPlaca | undefined>()
  const [codigo, setCodigo] = useState('')
  const [pagina, setPagina] = useState(1)
  const placas = usePlacas({ estado, codigo: codigo || undefined, pagina })
  const total = placas.data?.total ?? 0
  const paginas = Math.max(1, Math.ceil(total / 50))

  return (
    <div className={dono ? 'grid gap-4 lg:grid-cols-[1fr_20rem]' : 'grid gap-4'}>
      <div>
        <Cabecalho titulo={dono ? 'Estoque' : 'Minhas placas'} />
        <div className="mb-3 flex flex-wrap gap-2">
          <Select value={estado ?? TODOS} onValueChange={(v) => { setEstado(v === TODOS ? undefined : v as EstadoDaPlaca); setPagina(1) }}>
            <SelectTrigger aria-label="Situação" className="w-44"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value={TODOS}>Todas ({Object.values(placas.data?.porEstado ?? {}).reduce((a, b) => a + (b ?? 0), 0)})</SelectItem>
              {ESTADOS.map((e) => <SelectItem key={e} value={e}>{ROTULO_DO_ESTADO[e]} ({placas.data?.porEstado[e] ?? 0})</SelectItem>)}
            </SelectContent>
          </Select>
          <Input aria-label="Buscar código" placeholder="Código" className="w-36 uppercase" value={codigo} onChange={(e) => { setCodigo(e.target.value); setPagina(1) }} />
        </div>
        <Card>
          <CardContent>
            <EstadoDaConsulta carregando={placas.isPending} erro={placas.error} vazio={total === 0} textoVazio="Nenhuma placa aqui. Ative um lote em Pedidos de placas." />
            {total > 0 && (
              <Rolagem>
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Código</TableHead>
                      <TableHead>Situação</TableHead>
                      <TableHead>Com</TableHead>
                      <TableHead>Vence em</TableHead>
                      <TableHead className="text-right">Leituras</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {placas.data?.placas.map((p) => (
                      <TableRow key={p.codigo}>
                        <TableCell><Link className="font-medium tabular-nums underline-offset-4 hover:underline" to={`/placa/${p.codigo}`}>{p.codigo}</Link></TableCell>
                        <TableCell><EtiquetaDoEstado estado={p.estado} /></TableCell>
                        <TableCell>{p.consignadaANome ?? '-'}</TableCell>
                        <TableCell>{formatarData(p.venceEm)}</TableCell>
                        <TableCell className="text-right tabular-nums">{p.totalLeituras}</TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </Rolagem>
            )}
            {paginas > 1 && (
              <div className="mt-3 flex items-center justify-end gap-2 text-sm">
                <Button variant="outline" disabled={pagina <= 1} onClick={() => setPagina(pagina - 1)}>Anterior</Button>
                <span className="tabular-nums">{pagina} de {paginas}</span>
                <Button variant="outline" disabled={pagina >= paginas} onClick={() => setPagina(pagina + 1)}>Próxima</Button>
              </div>
            )}
          </CardContent>
        </Card>
      </div>
      {dono && <Consignacao />}
    </div>
  )
}

/** Dar placas ao vendedor, pela quantidade ou pelos códigos, e receber de volta. */
function Consignacao() {
  const equipe = useEquipe()
  const consignar = useConsignar()
  const devolver = useDevolver()
  const [vendedorId, setVendedorId] = useState('')
  const [quantidade, setQuantidade] = useState('5')
  const [codigos, setCodigos] = useState('')
  const listaDeCodigos = codigos.split(/[\s,;]+/).map((c) => c.trim()).filter(Boolean)
  const ativos = (equipe.data ?? []).filter((v) => v.ativo)

  return (
    <Card className="self-start">
      <CardHeader><CardTitle>Consignar ao vendedor</CardTitle></CardHeader>
      <CardContent>
        <form
          className="grid gap-4"
          onSubmit={(e) => {
            e.preventDefault()
            consignar.mutate(listaDeCodigos.length > 0 ? { vendedorId, codigos: listaDeCodigos } : { vendedorId, quantidade: Number(quantidade) }, { onSuccess: () => setCodigos('') })
          }}
        >
          <Erro erro={consignar.error || devolver.error ? mensagemDoErro(consignar.error ?? devolver.error) : null} />
          {(consignar.data || devolver.data) && (
            <Alert><AlertDescription>{consignar.data ? `${consignar.data.placas} placas com o vendedor.` : `${devolver.data?.placas} placas de volta ao estoque.`}</AlertDescription></Alert>
          )}
          <Field>
            <FieldLabel htmlFor="vendedor">Vendedor</FieldLabel>
            <Select value={vendedorId} onValueChange={setVendedorId}>
              <SelectTrigger id="vendedor" className="w-full"><SelectValue placeholder={ativos.length ? 'Escolha' : 'Cadastre a equipe antes'} /></SelectTrigger>
              <SelectContent>{ativos.map((v) => <SelectItem key={v.id} value={v.id}>{v.nome}</SelectItem>)}</SelectContent>
            </Select>
          </Field>
          <Field>
            <FieldLabel htmlFor="quantidade">Quantidade</FieldLabel>
            <Input id="quantidade" inputMode="numeric" disabled={listaDeCodigos.length > 0} value={quantidade} onChange={(e) => setQuantidade(e.target.value.replace(/\D/g, ''))} />
          </Field>
          <Field>
            <FieldLabel htmlFor="codigos">Ou os códigos</FieldLabel>
            <Textarea id="codigos" rows={3} className="uppercase" placeholder="K7M2PX, 3GKD93" value={codigos} onChange={(e) => setCodigos(e.target.value)} />
          </Field>
          <Button type="submit" disabled={!vendedorId || consignar.isPending}>Consignar</Button>
          <Button type="button" variant="outline" disabled={!vendedorId || devolver.isPending} onClick={() => devolver.mutate(vendedorId)}>Devolver ao estoque</Button>
        </form>
      </CardContent>
    </Card>
  )
}
