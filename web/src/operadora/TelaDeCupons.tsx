import { type FormEvent, useState } from 'react'
import { mensagemDoErro } from '@/api/cliente'
import { formatarBps, formatarData, paraBps } from '@/api/rotulos'
import type { Cupom } from '@/api/tipos'
import { Cabecalho, Erro, EstadoDaConsulta, Rolagem } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { formatarCentavos, paraCentavos } from '@/dinheiro/dinheiro'
import { useAlterarCupom, useAtivarCupom, useCriarCupom, useCupons, useExcluirCupom } from './consultas'

type Formulario = { codigo: string; tipo: 'percentual' | 'reais'; desconto: string; validoAte: string; usos: string }
const VAZIO: Formulario = { codigo: '', tipo: 'percentual', desconto: '', validoAte: '', usos: '' }

/** Cupons de desconto no pedido de placas. Cupom usado é desativado, e não excluído. */
export function TelaDeCupons() {
  const cupons = useCupons()
  const criar = useCriarCupom()
  const alterar = useAlterarCupom()
  const ativar = useAtivarCupom()
  const excluir = useExcluirCupom()
  const [editando, setEditando] = useState<Cupom | null>(null)
  const [dados, setDados] = useState<Formulario>(VAZIO)
  const erro = criar.error ?? alterar.error ?? ativar.error ?? excluir.error
  const desconto = dados.tipo === 'percentual' ? paraBps(dados.desconto) : paraCentavos(dados.desconto)

  function editar(c: Cupom | null) {
    setEditando(c)
    setDados(c
      ? {
          codigo: c.codigo,
          tipo: c.descontoBps !== null ? 'percentual' : 'reais',
          desconto: c.descontoBps !== null ? String(c.descontoBps / 100).replace('.', ',') : ((c.descontoCents ?? 0) / 100).toFixed(2).replace('.', ','),
          validoAte: c.validoAte?.slice(0, 10) ?? '',
          usos: c.usosMaximos ? String(c.usosMaximos) : '',
        }
      : VAZIO)
  }

  async function salvar(evento: FormEvent) {
    evento.preventDefault()
    if (desconto === null) return
    const cupom = {
      codigo: dados.codigo,
      descontoBps: dados.tipo === 'percentual' ? desconto : null,
      descontoCents: dados.tipo === 'reais' ? desconto : null,
      // Vale até o fim do dia escolhido, no fuso de quem cadastra.
      validoAte: dados.validoAte ? new Date(`${dados.validoAte}T23:59:59`).toISOString() : null,
      usosMaximos: dados.usos ? Number(dados.usos) : null,
    }
    try {
      await (editando ? alterar.mutateAsync({ id: editando.id, ...cupom }) : criar.mutateAsync(cupom))
      editar(null)
    } catch {
      // O erro aparece pelo estado da gravação.
    }
  }

  return (
    <div className="grid grid-cols-1 gap-4 lg:grid-cols-[minmax(0,1fr)_20rem]">
      <div>
        <Cabecalho titulo="Cupons" />
        <Erro erro={erro ? mensagemDoErro(erro) : null} />
        <Card className="mt-3">
          <CardContent>
            <EstadoDaConsulta carregando={cupons.isPending} erro={cupons.error} vazio={cupons.data?.length === 0} textoVazio="Nenhum cupom ainda." />
            {cupons.data && cupons.data.length > 0 && (
              <Rolagem>
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Código</TableHead>
                      <TableHead className="text-right">Desconto</TableHead>
                      <TableHead>Vale até</TableHead>
                      <TableHead className="text-right">Usos</TableHead>
                      <TableHead>Situação</TableHead>
                      <TableHead><span className="sr-only">Ações</span></TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {cupons.data.map((c) => (
                      <TableRow key={c.id}>
                        <TableCell className="font-medium tabular-nums">{c.codigo}</TableCell>
                        <TableCell className="text-right tabular-nums">{c.descontoBps !== null ? formatarBps(c.descontoBps) : formatarCentavos(c.descontoCents ?? 0)}</TableCell>
                        <TableCell>{c.validoAte ? formatarData(c.validoAte) : 'Sem prazo'}</TableCell>
                        <TableCell className="text-right tabular-nums">{c.usos}{c.usosMaximos ? ` de ${c.usosMaximos}` : ''}</TableCell>
                        <TableCell><Badge variant={c.ativo ? 'default' : 'outline'}>{c.ativo ? 'Ativo' : 'Desligado'}</Badge></TableCell>
                        <TableCell>
                          <div className="flex justify-end gap-2">
                            <Button variant="outline" onClick={() => editar(c)}>Editar</Button>
                            <Button variant="outline" onClick={() => ativar.mutate({ id: c.id, ativo: !c.ativo })}>{c.ativo ? 'Desligar' : 'Ligar'}</Button>
                            {c.usos === 0 && <Button variant="ghost" onClick={() => excluir.mutate(c.id)}>Excluir</Button>}
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
      </div>
      <Card className="self-start">
        <CardHeader><CardTitle>{editando ? `Editar ${editando.codigo}` : 'Novo cupom'}</CardTitle></CardHeader>
        <CardContent>
          <form className="grid gap-4" onSubmit={salvar}>
            <Field>
              <FieldLabel htmlFor="codigo">Código</FieldLabel>
              <Input id="codigo" className="uppercase" disabled={!!editando} value={dados.codigo} onChange={(e) => setDados({ ...dados, codigo: e.target.value })} />
            </Field>
            <Tabs value={dados.tipo} onValueChange={(t) => setDados({ ...dados, tipo: t as Formulario['tipo'], desconto: '' })}>
              <TabsList className="w-full">
                <TabsTrigger value="percentual">Percentual</TabsTrigger>
                <TabsTrigger value="reais">Em reais</TabsTrigger>
              </TabsList>
            </Tabs>
            <Field>
              <FieldLabel htmlFor="desconto">{dados.tipo === 'percentual' ? 'Desconto (%)' : 'Desconto (R$)'}</FieldLabel>
              <Input id="desconto" inputMode="decimal" value={dados.desconto} onChange={(e) => setDados({ ...dados, desconto: e.target.value })} />
            </Field>
            <Field>
              <FieldLabel htmlFor="validoAte">Vale até</FieldLabel>
              <Input id="validoAte" type="date" value={dados.validoAte} onChange={(e) => setDados({ ...dados, validoAte: e.target.value })} />
            </Field>
            <Field>
              <FieldLabel htmlFor="usos">Limite de usos</FieldLabel>
              <Input id="usos" inputMode="numeric" placeholder="Sem limite" value={dados.usos} onChange={(e) => setDados({ ...dados, usos: e.target.value.replace(/\D/g, '') })} />
            </Field>
            <Button type="submit" disabled={dados.codigo.trim().length < 3 || desconto === null}>{editando ? 'Salvar cupom' : 'Criar cupom'}</Button>
            {editando && <Button type="button" variant="ghost" onClick={() => editar(null)}>Cancelar</Button>}
          </form>
        </CardContent>
      </Card>
    </div>
  )
}
