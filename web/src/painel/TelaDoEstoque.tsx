import { useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { mensagemDoErro } from '@/api/cliente'
import { formatarData, ROTULO_DA_BAIXA, ROTULO_DA_CATEGORIA, ROTULO_DO_ESTADO } from '@/api/rotulos'
import type { EstadoDaPlaca, MotivoDaBaixa } from '@/api/tipos'
import { AcoesDaLinha } from '@/componentes/AcoesDaLinha'
import { Badge } from '@/components/ui/badge'
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
import { useArquivarLoteDaRevenda, useBaixarPlaca, useConsignar, useDesfazerBaixa, useDevolver, useEquipe, useLotesDaRevenda, usePlacas } from './consultas'

const ESTADOS: EstadoDaPlaca[] = ['EmEstoque', 'Ativa', 'Carencia', 'Vencida', 'Suspensa', 'Baixada']
const MOTIVOS = Object.keys(ROTULO_DA_BAIXA) as MotivoDaBaixa[]
const TODOS = 'todos'

/** O estoque do revendedor e a consignação ao vendedor. O vendedor vê só as dele. */
export function TelaDoEstoque() {
  const dono = useSessao((s) => s.perfil?.papel === 'Dono')
  const [estado, setEstado] = useState<EstadoDaPlaca | undefined>()
  const [codigo, setCodigo] = useState('')
  const [com, setCom] = useState<string | undefined>()
  const [pagina, setPagina] = useState(1)
  const placas = usePlacas({ estado, codigo: codigo || undefined, consignadaA: com, pagina })
  const equipe = useEquipe()
  const navegar = useNavigate()
  const baixar = useBaixarPlaca()
  const desfazer = useDesfazerBaixa()
  const total = placas.data?.total ?? 0
  const paginas = Math.max(1, Math.ceil(total / 50))

  return (
    <div className="grid gap-4">
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
          {dono && (
            <Select value={com ?? TODOS} onValueChange={(v) => { setCom(v === TODOS ? undefined : v); setPagina(1) }}>
              <SelectTrigger aria-label="Com quem" className="w-44"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value={TODOS}>Com todos</SelectItem>
                {(equipe.data ?? []).map((v) => <SelectItem key={v.id} value={v.id}>{v.nome}</SelectItem>)}
              </SelectContent>
            </Select>
          )}
          <Input type="search" aria-label="Buscar código" placeholder="Código" className="w-36 uppercase" value={codigo} onChange={(e) => { setCodigo(e.target.value); setPagina(1) }} />
        </div>
        <Erro erro={baixar.error || desfazer.error ? mensagemDoErro(baixar.error ?? desfazer.error) : null} />
        <Card>
          <CardContent>
            <EstadoDaConsulta carregando={placas.isPending} erro={placas.error} vazio={total === 0} textoVazio="Nenhuma placa."
              acao={dono && !estado && !codigo && <Button asChild variant="outline"><Link to="/painel/pedidos">Pedir placas</Link></Button>} />
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
                      {dono && <TableHead><span className="sr-only">Ações</span></TableHead>}
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {placas.data?.placas.map((p) => (
                      <TableRow key={p.codigo}>
                        <TableCell><Link className="font-medium tabular-nums underline-offset-4 hover:underline" to={`/placa/${p.codigo}`}>{p.codigo}</Link></TableCell>
                        <TableCell>
                          <span className="flex flex-wrap items-center gap-1.5">
                            <EtiquetaDoEstado estado={p.estado} largura />
                            {p.motivoDaBaixa && <span className="text-xs text-muted-foreground">{ROTULO_DA_BAIXA[p.motivoDaBaixa] ?? MOTIVO_DO_SISTEMA[p.motivoDaBaixa]}</span>}
                          </span>
                        </TableCell>
                        <TableCell>{p.consignadaANome ?? '-'}</TableCell>
                        <TableCell>{formatarData(p.venceEm)}</TableCell>
                        <TableCell className="text-right tabular-nums">{p.totalLeituras}</TableCell>
                        {dono && (
                          <TableCell>
                            <AcoesDaLinha nome={p.codigo} outras={[
                              { rotulo: 'Abrir a ficha', aoClicar: () => navegar(`/placa/${p.codigo}`) },
                              ...(p.estado === 'EmEstoque' ? MOTIVOS.map((m) => ({ rotulo: `Baixa: ${ROTULO_DA_BAIXA[m]}`, destrutiva: true, aoClicar: () => baixar.mutate({ codigo: p.codigo, motivo: m }) })) : []),
                              p.estado === 'Baixada' && !!p.motivoDaBaixa && p.motivoDaBaixa in ROTULO_DA_BAIXA && { rotulo: 'Desfazer baixa', aoClicar: () => desfazer.mutate(p.codigo) },
                            ]} />
                          </TableCell>
                        )}
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
      {dono && (
        <div className="grid items-start gap-4 lg:grid-cols-2">
          <Consignacao />
          <LotesDaRevenda />
        </div>
      )}
    </div>
  )
}

/** Baixa que o dono não escolhe: vem da troca, do lote arquivado ou do encerramento. */
const MOTIVO_DO_SISTEMA: Record<MotivoDaBaixa, string> = {
  Troca: 'Trocada',
  DefeitoDeImpressao: 'Defeito de impressão',
  Perdida: 'Perdida',
  Quebrada: 'Quebrada',
  NaoImpressa: 'Não impressa',
  LoteArquivado: 'Lote arquivado',
  Encerramento: 'Conta encerrada',
}

/** Os lotes que vieram para a revenda: o de teste ou de campanha que não saiu se arquiva aqui. */
function LotesDaRevenda() {
  const lotes = useLotesDaRevenda()
  const arquivar = useArquivarLoteDaRevenda()
  if (!lotes.data?.length) return null

  return (
    <Card>
      <CardHeader><CardTitle>Lotes</CardTitle></CardHeader>
      <CardContent className="grid gap-3">
        <Erro erro={arquivar.error ? mensagemDoErro(arquivar.error) : null} />
        <Rolagem>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Lote</TableHead>
                <TableHead className="text-right">Em estoque</TableHead>
                <TableHead className="text-right">Baixadas</TableHead>
                <TableHead><span className="sr-only">Ações</span></TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {lotes.data.map((l) => (
                <TableRow key={l.id}>
                  <TableCell className="whitespace-normal">
                    <span className="font-medium tabular-nums">{l.rotulo}</span>
                    {l.categoria !== 'Placa' && <Badge variant="outline" className="ml-2">{ROTULO_DA_CATEGORIA[l.categoria]}</Badge>}
                    {l.titulo && <span className="block text-xs text-muted-foreground">{l.titulo}</span>}
                    {l.arquivadoEm && <span className="block text-xs text-muted-foreground">Arquivado</span>}
                  </TableCell>
                  <TableCell className="text-right tabular-nums">{l.emEstoque}</TableCell>
                  <TableCell className="text-right tabular-nums">{l.baixadas}</TableCell>
                  <TableCell>
                    <AcoesDaLinha nome={l.rotulo} outras={[
                      { rotulo: l.arquivadoEm ? 'Desarquivar' : 'Arquivar', desabilitada: arquivar.isPending, aoClicar: () => arquivar.mutate({ id: l.id, arquivado: !l.arquivadoEm }) },
                    ]} />
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Rolagem>
      </CardContent>
    </Card>
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
    <Card>
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
