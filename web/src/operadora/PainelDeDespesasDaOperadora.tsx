import { zodResolver } from '@hookform/resolvers/zod'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { mensagemDoErro } from '@/api/cliente'
import { ROTULO_DA_DESPESA_DA_OPERADORA, formatarData } from '@/api/rotulos'
import type { CategoriaDeDespesaDaOperadora, DespesaDaOperadora } from '@/api/tipos'
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
import { hoje } from '@/financeiro/fatias'
import { reais, texto } from '@/lib/esquemas'
import { useDespesasDaOperadora, useExcluirDespesaDaOperadora, useSalvarDespesaDaOperadora } from './consultasFinanceiras'

const esquema = z.object({
  data: z.string().regex(/^\d{4}-\d{2}-\d{2}$/, 'Escolha a data.'),
  descricao: texto('a descrição'),
  valor: reais,
})
type Formulario = z.infer<typeof esquema>

const centavosEmTexto = (c: number) => (c / 100).toFixed(2).replace('.', ',')

/** A despesa da operadora, nova ou para editar, num diálogo: no celular ocupa a tela, no computador fica no meio. */
function FormularioDeDespesa({ despesa, aoFechar }: { despesa: DespesaDaOperadora | null; aoFechar: () => void }) {
  const salvar = useSalvarDespesaDaOperadora()
  const [categoria, setCategoria] = useState<CategoriaDeDespesaDaOperadora>(despesa?.categoria ?? 'GraficaEMaterial')
  const formulario = useForm<Formulario>({
    resolver: zodResolver(esquema),
    mode: 'onChange',
    defaultValues: despesa ? { data: despesa.data, descricao: despesa.descricao, valor: centavosEmTexto(despesa.valorCents) } : { data: hoje(), descricao: '', valor: '' },
  })

  async function enviar(dados: Formulario) {
    try {
      await salvar.mutateAsync({ id: despesa?.id, data: dados.data, descricao: dados.descricao, categoria, valorCents: paraCentavos(dados.valor) ?? 0 })
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
            <Select value={categoria} onValueChange={(v) => setCategoria(v as CategoriaDeDespesaDaOperadora)}>
              <SelectTrigger id="categoria-da-despesa" className="w-full"><SelectValue /></SelectTrigger>
              <SelectContent>
                {(Object.keys(ROTULO_DA_DESPESA_DA_OPERADORA) as CategoriaDeDespesaDaOperadora[]).map((c) => <SelectItem key={c} value={c}>{ROTULO_DA_DESPESA_DA_OPERADORA[c]}</SelectItem>)}
              </SelectContent>
            </Select>
          </Field>
          <Campo formulario={formulario} nome="descricao" rotulo="Descrição" autoComplete="off" />
          <Campo formulario={formulario} nome="valor" rotulo="Valor (R$)" inputMode="decimal" placeholder="0,00" />
          <Button type="submit" disabled={salvar.isPending}>{despesa ? 'Salvar despesa' : 'Lançar despesa'}</Button>
        </form>
      </DialogContent>
    </Dialog>
  )
}

/** As despesas da operadora no mês escolhido, com editar e excluir. Corrigir mês fechado lança a diferença no mês corrente. */
export function PainelDeDespesasDaOperadora({ competencia }: { competencia: string | undefined }) {
  const despesas = useDespesasDaOperadora(competencia)
  const excluir = useExcluirDespesaDaOperadora()
  const [aberta, setAberta] = useState<DespesaDaOperadora | 'nova' | null>(null)

  return (
    <div className="grid gap-3">
      <div className="flex justify-end">
        <Button className="max-sm:w-full" onClick={() => setAberta('nova')}>Nova despesa</Button>
      </div>
      <Erro erro={excluir.error ? mensagemDoErro(excluir.error) : null} />
      <Card>
        <CardContent>
          <ListaPaginada lista={despesas} textoVazio="Nenhuma despesa da operadora no mês." nomeDaBusca="Buscar nas despesas" placeholder="Descrição ou categoria">
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
                          <p className="text-xs text-muted-foreground">{ROTULO_DA_DESPESA_DA_OPERADORA[d.categoria]}</p>
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
      {aberta && <FormularioDeDespesa despesa={aberta === 'nova' ? null : aberta} aoFechar={() => setAberta(null)} />}
    </div>
  )
}
