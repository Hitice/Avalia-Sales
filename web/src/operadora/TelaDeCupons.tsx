import { zodResolver } from '@hookform/resolvers/zod'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { mensagemDoErro } from '@/api/cliente'
import { formatarBps, formatarData, paraBps } from '@/api/rotulos'
import type { Cupom } from '@/api/tipos'
import { Campo } from '@/componentes/Campo'
import { Cabecalho, Erro, EstadoDaConsulta, Rolagem } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { formatarCentavos, paraCentavos } from '@/dinheiro/dinheiro'
import { useAlterarCupom, useAtivarCupom, useCriarCupom, useCupons, useExcluirCupom } from './consultas'

const esquema = z.object({
  codigo: z.string().trim().min(3, 'Use 3 caracteres ou mais.').max(30, 'Use até 30 caracteres.'),
  tipo: z.enum(['percentual', 'reais']),
  desconto: z.string(),
  validoAte: z.string(),
  usos: z.string().trim().regex(/^\d*$/, 'Só números.'),
}).superRefine((d, ctx) => {
  const valor = d.tipo === 'percentual' ? paraBps(d.desconto) : paraCentavos(d.desconto)
  if (valor === null || valor === 0) {
    ctx.addIssue({ code: 'custom', path: ['desconto'], message: d.tipo === 'percentual' ? 'Digite em %, como 10.' : 'Digite o valor, como 20,00.' })
  }
})
type Formulario = z.infer<typeof esquema>
const VAZIO: Formulario = { codigo: '', tipo: 'percentual', desconto: '', validoAte: '', usos: '' }

/** Cupons de desconto no pedido de placas. Cupom usado é desativado, e não excluído. */
export function TelaDeCupons() {
  const cupons = useCupons()
  const criar = useCriarCupom()
  const alterar = useAlterarCupom()
  const ativar = useAtivarCupom()
  const excluir = useExcluirCupom()
  const [editando, setEditando] = useState<Cupom | null>(null)
  const formulario = useForm<Formulario>({ resolver: zodResolver(esquema), mode: 'onChange', defaultValues: VAZIO })
  const tipo = formulario.watch('tipo')
  const erro = criar.error ?? alterar.error ?? ativar.error ?? excluir.error

  function editar(c: Cupom | null) {
    setEditando(c)
    formulario.reset(c
      ? {
          codigo: c.codigo,
          tipo: c.descontoBps !== null ? 'percentual' : 'reais',
          desconto: c.descontoBps !== null ? String(c.descontoBps / 100).replace('.', ',') : ((c.descontoCents ?? 0) / 100).toFixed(2).replace('.', ','),
          validoAte: c.validoAte?.slice(0, 10) ?? '',
          usos: c.usosMaximos ? String(c.usosMaximos) : '',
        }
      : VAZIO)
  }

  async function salvar(dados: Formulario) {
    const desconto = dados.tipo === 'percentual' ? paraBps(dados.desconto) : paraCentavos(dados.desconto)
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
          <form className="grid gap-4" onSubmit={formulario.handleSubmit(salvar)} noValidate>
            <Campo formulario={formulario} nome="codigo" rotulo="Código" className="uppercase" disabled={!!editando} />
            <Tabs value={tipo} onValueChange={(t) => {
              formulario.setValue('tipo', t as Formulario['tipo'])
              formulario.setValue('desconto', '', { shouldValidate: formulario.formState.isSubmitted })
            }}>
              <TabsList className="w-full">
                <TabsTrigger value="percentual">Percentual</TabsTrigger>
                <TabsTrigger value="reais">Em reais</TabsTrigger>
              </TabsList>
            </Tabs>
            <Campo formulario={formulario} nome="desconto" rotulo={tipo === 'percentual' ? 'Desconto (%)' : 'Desconto (R$)'} inputMode="decimal" />
            <Campo formulario={formulario} nome="validoAte" rotulo="Vale até" type="date" conferir={false} />
            <Campo formulario={formulario} nome="usos" rotulo="Limite de usos" inputMode="numeric" placeholder="Sem limite" conferir={false} />
            <Button type="submit" disabled={criar.isPending || alterar.isPending}>{editando ? 'Salvar cupom' : 'Criar cupom'}</Button>
            {editando && <Button type="button" variant="ghost" onClick={() => editar(null)}>Cancelar</Button>}
          </form>
        </CardContent>
      </Card>
    </div>
  )
}
