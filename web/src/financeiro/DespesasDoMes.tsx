import { zodResolver } from '@hookform/resolvers/zod'
import type { UseMutationResult } from '@tanstack/react-query'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { mensagemDoErro } from '@/api/cliente'
import { formatarData } from '@/api/rotulos'
import { AcoesDaLinha } from '@/componentes/AcoesDaLinha'
import { Campo } from '@/componentes/Campo'
import { ListaPaginada } from '@/componentes/ListaPaginada'
import { Erro, Rolagem } from '@/componentes/Tela'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Dialog, DialogContent, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Field, FieldLabel } from '@/components/ui/field'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatarCentavos, paraCentavos } from '@/dinheiro/dinheiro'
import type { EstadoDaLista } from '@/lib/paginacao'
import { reais, texto } from '@/lib/esquemas'
import { hoje } from './fatias'

const esquema = z.object({
  data: z.string().regex(/^\d{4}-\d{2}-\d{2}$/, 'Escolha a data.'),
  descricao: texto('a descrição'),
  valor: reais,
})
type Formulario = z.infer<typeof esquema>

const centavosEmTexto = (c: number) => (c / 100).toFixed(2).replace('.', ',')

/** O que a despesa da revenda e a da operadora têm em comum; a da operadora traz o fornecedor. */
export type DespesaDoMes<C extends string> = {
  id: string
  data: string
  descricao: string
  categoria: C
  valorCents: number
  fornecedorId?: string | null
  fornecedor?: string | null
}

export type GravacaoDeDespesa<C extends string> = { id?: string; data: string; descricao: string; categoria: C; valorCents: number; fornecedorId?: string | null }

/** O campo do fornecedor, quando a despesa tem um (só a da operadora). */
export type CampoDoFornecedor = (props: { valor: string | null; atual: { id: string; nome: string } | null; aoMudar: (id: string | null) => void }) => React.ReactNode

type Props<C extends string, D extends DespesaDoMes<C>> = {
  lista: EstadoDaLista<D>
  salvar: UseMutationResult<unknown, Error, GravacaoDeDespesa<C>>
  excluir: UseMutationResult<unknown, Error, string>
  rotulos: Record<C, string>
  categoriaPadrao: C
  textoVazio: string
  fornecedor?: CampoDoFornecedor
}

/** A despesa, nova ou para editar, num diálogo: no celular ocupa a tela, no computador fica no meio. */
function FormularioDeDespesa<C extends string, D extends DespesaDoMes<C>>({ despesa, aoFechar, salvar, rotulos, categoriaPadrao, fornecedor }:
  Pick<Props<C, D>, 'salvar' | 'rotulos' | 'categoriaPadrao' | 'fornecedor'> & { despesa: D | null; aoFechar: () => void }) {
  const [categoria, setCategoria] = useState<C>(despesa?.categoria ?? categoriaPadrao)
  const [fornecedorId, setFornecedorId] = useState<string | null>(despesa?.fornecedorId ?? null)
  const formulario = useForm<Formulario>({
    resolver: zodResolver(esquema),
    mode: 'onChange',
    defaultValues: despesa ? { data: despesa.data, descricao: despesa.descricao, valor: centavosEmTexto(despesa.valorCents) } : { data: hoje(), descricao: '', valor: '' },
  })

  async function enviar(dados: Formulario) {
    try {
      await salvar.mutateAsync({
        id: despesa?.id, data: dados.data, descricao: dados.descricao, categoria, valorCents: paraCentavos(dados.valor) ?? 0,
        ...(fornecedor ? { fornecedorId } : {}),
      })
      aoFechar()
    } catch {
      // O erro aparece pelo estado da gravação.
    }
  }

  return (
    <Dialog open onOpenChange={(aberto) => !aberto && aoFechar()}>
      <DialogContent>
        <DialogHeader><DialogTitle>{despesa ? 'Editar despesa' : 'Nova despesa'}</DialogTitle></DialogHeader>
        <form className="grid gap-4" onSubmit={formulario.handleSubmit(enviar)} noValidate>
          <Erro erro={salvar.error ? mensagemDoErro(salvar.error) : null} />
          <Campo formulario={formulario} nome="data" rotulo="Data" type="date" max={hoje()} />
          <Field>
            <FieldLabel htmlFor="categoria-da-despesa">Categoria</FieldLabel>
            <Select value={categoria} onValueChange={(v) => setCategoria(v as C)}>
              <SelectTrigger id="categoria-da-despesa" className="w-full"><SelectValue /></SelectTrigger>
              <SelectContent>
                {(Object.keys(rotulos) as C[]).map((c) => <SelectItem key={c} value={c}>{rotulos[c]}</SelectItem>)}
              </SelectContent>
            </Select>
          </Field>
          {fornecedor?.({ valor: fornecedorId, atual: despesa?.fornecedorId && despesa.fornecedor ? { id: despesa.fornecedorId, nome: despesa.fornecedor } : null, aoMudar: setFornecedorId })}
          <Campo formulario={formulario} nome="descricao" rotulo="Descrição" autoComplete="off" />
          <Campo formulario={formulario} nome="valor" rotulo="Valor (R$)" inputMode="decimal" placeholder="0,00" />
          <Button type="submit" disabled={salvar.isPending}>{despesa ? 'Salvar despesa' : 'Lançar despesa'}</Button>
        </form>
      </DialogContent>
    </Dialog>
  )
}

/**
 * As despesas do mês escolhido, com editar e excluir, da revenda ou da operadora. Corrigir mês
 * fechado lança a diferença no mês corrente.
 */
export function DespesasDoMes<C extends string, D extends DespesaDoMes<C>>({ lista, salvar, excluir, rotulos, categoriaPadrao, textoVazio, fornecedor }: Props<C, D>) {
  const [aberta, setAberta] = useState<D | 'nova' | null>(null)

  return (
    <div className="grid gap-3">
      <div className="flex justify-end">
        <Button className="max-sm:w-full" onClick={() => setAberta('nova')}>Nova despesa</Button>
      </div>
      <Erro erro={excluir.error ? mensagemDoErro(excluir.error) : null} />
      <Card>
        <CardContent>
          <ListaPaginada lista={lista} textoVazio={textoVazio} nomeDaBusca="Buscar nas despesas" placeholder="Descrição ou categoria">
            {(itens) => (
              <Rolagem>
                <Table semBusca>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Despesa</TableHead>
                      <TableHead tipo="data">Data</TableHead>
                      <TableHead tipo="numero">Valor</TableHead>
                      <TableHead tipo="acoes" />
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {itens.map((d) => (
                      <TableRow key={d.id}>
                        <TableCell className="min-w-40 whitespace-normal">
                          <p className="font-medium">{d.descricao}</p>
                          <p className="text-xs text-muted-foreground">{[rotulos[d.categoria], d.fornecedor].filter(Boolean).join(' - ')}</p>
                        </TableCell>
                        <TableCell>{formatarData(`${d.data}T12:00:00`)}</TableCell>
                        <TableCell>{formatarCentavos(d.valorCents)}</TableCell>
                        <TableCell>
                          <AcoesDaLinha nome={d.descricao} principal={{ rotulo: 'Editar', aoClicar: () => setAberta(d) }}
                            outras={[{ rotulo: 'Excluir', destrutiva: true, desabilitada: excluir.isPending, aoClicar: () => excluir.mutate(d.id) }]} />
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
      {aberta && (
        <FormularioDeDespesa despesa={aberta === 'nova' ? null : aberta} aoFechar={() => setAberta(null)}
          salvar={salvar} rotulos={rotulos} categoriaPadrao={categoriaPadrao} fornecedor={fornecedor} />
      )}
    </div>
  )
}
