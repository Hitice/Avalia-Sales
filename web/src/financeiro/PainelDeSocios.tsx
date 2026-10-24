import { zodResolver } from '@hookform/resolvers/zod'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { cpfValido } from '@/acesso/documentos'
import { mensagemDoErro } from '@/api/cliente'
import { ROTULO_DA_RETIRADA, formatarData } from '@/api/rotulos'
import type { NaturezaDaRetirada, Retirada, Socio } from '@/api/tipos'
import { AcoesDaLinha } from '@/componentes/AcoesDaLinha'
import { Campo } from '@/componentes/Campo'
import { Erro, EstadoDaConsulta } from '@/componentes/Tela'
import { Button } from '@/components/ui/button'
import { Card, CardAction, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Dialog, DialogContent, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Field, FieldLabel } from '@/components/ui/field'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatarCentavos, paraCentavos } from '@/dinheiro/dinheiro'
import { reais, texto } from '@/lib/esquemas'
import { digitos, mascaraCpf } from '@/lib/mascaras'
import { hoje } from './fatias'
import { useExcluirRetirada, useExcluirSocio, useRetiradas, useSalvarRetirada, useSalvarSocio, useSocios } from './consultas'

const esquemaDoSocio = z.object({
  nome: texto('o nome'),
  cpf: z.string().trim().refine((t) => !t || cpfValido(digitos(t)), 'CPF com dígito errado.'),
})
type FormularioDoSocio = z.infer<typeof esquemaDoSocio>

function FormularioDeSocio({ socio, aoFechar }: { socio: Socio | null; aoFechar: () => void }) {
  const salvar = useSalvarSocio()
  const formulario = useForm<FormularioDoSocio>({
    resolver: zodResolver(esquemaDoSocio),
    mode: 'onChange',
    defaultValues: { nome: socio?.nome ?? '', cpf: socio?.cpf ? mascaraCpf(socio.cpf) : '' },
  })

  async function enviar(dados: FormularioDoSocio) {
    try {
      await salvar.mutateAsync({ id: socio?.id, nome: dados.nome, cpf: digitos(dados.cpf) || null })
      aoFechar()
    } catch {
      // O erro aparece pelo estado da gravação.
    }
  }

  return (
    <Dialog open onOpenChange={(aberto) => !aberto && aoFechar()}>
      <DialogContent>
        <DialogHeader><DialogTitle>{socio ? 'Editar sócio' : 'Sócio do contrato'}</DialogTitle></DialogHeader>
        <form className="grid gap-4" onSubmit={formulario.handleSubmit(enviar)} noValidate>
          <Erro erro={salvar.error ? mensagemDoErro(salvar.error) : null} />
          <Campo formulario={formulario} nome="nome" rotulo="Nome, como no contrato social" autoComplete="off" />
          <Campo formulario={formulario} nome="cpf" rotulo="CPF (opcional)" inputMode="numeric" mascara={mascaraCpf} />
          <Button type="submit" disabled={salvar.isPending}>{socio ? 'Salvar sócio' : 'Cadastrar sócio'}</Button>
        </form>
      </DialogContent>
    </Dialog>
  )
}

const esquemaDaRetirada = z.object({
  data: z.string().regex(/^\d{4}-\d{2}-\d{2}$/, 'Escolha a data.'),
  valor: reais,
  observacao: z.string().trim().max(120, 'Use até 120 caracteres.'),
})
type FormularioDaRetirada = z.infer<typeof esquemaDaRetirada>

function FormularioDeRetirada({ retirada, socios, aoFechar }: { retirada: Retirada | null; socios: Socio[]; aoFechar: () => void }) {
  const salvar = useSalvarRetirada()
  const [socioId, setSocioId] = useState(retirada?.socioId ?? socios[0]?.id ?? '')
  const [natureza, setNatureza] = useState<NaturezaDaRetirada>(retirada?.natureza ?? 'ProLabore')
  const formulario = useForm<FormularioDaRetirada>({
    resolver: zodResolver(esquemaDaRetirada),
    mode: 'onChange',
    defaultValues: retirada
      ? { data: retirada.data, valor: (retirada.valorCents / 100).toFixed(2).replace('.', ','), observacao: retirada.observacao ?? '' }
      : { data: hoje(), valor: '', observacao: '' },
  })

  async function enviar(dados: FormularioDaRetirada) {
    try {
      await salvar.mutateAsync({ id: retirada?.id, socioId, data: dados.data, natureza, valorCents: paraCentavos(dados.valor) ?? 0, observacao: dados.observacao || null })
      aoFechar()
    } catch {
      // O erro aparece pelo estado da gravação.
    }
  }

  return (
    <Dialog open onOpenChange={(aberto) => !aberto && aoFechar()}>
      <DialogContent>
        <DialogHeader><DialogTitle>{retirada ? 'Editar retirada' : 'Nova retirada'}</DialogTitle></DialogHeader>
        <form className="grid gap-4" onSubmit={formulario.handleSubmit(enviar)} noValidate>
          <Erro erro={salvar.error ? mensagemDoErro(salvar.error) : null} />
          <Field>
            <FieldLabel htmlFor="socio">Sócio</FieldLabel>
            <Select value={socioId} onValueChange={setSocioId}>
              <SelectTrigger id="socio" className="w-full"><SelectValue /></SelectTrigger>
              <SelectContent>{socios.map((s) => <SelectItem key={s.id} value={s.id}>{s.nome}</SelectItem>)}</SelectContent>
            </Select>
          </Field>
          <Field>
            <FieldLabel htmlFor="natureza">Natureza</FieldLabel>
            <Select value={natureza} onValueChange={(v) => setNatureza(v as NaturezaDaRetirada)}>
              <SelectTrigger id="natureza" className="w-full"><SelectValue /></SelectTrigger>
              <SelectContent>
                {(Object.keys(ROTULO_DA_RETIRADA) as NaturezaDaRetirada[]).map((n) => <SelectItem key={n} value={n}>{ROTULO_DA_RETIRADA[n]}</SelectItem>)}
              </SelectContent>
            </Select>
          </Field>
          <Campo formulario={formulario} nome="data" rotulo="Data" type="date" max={hoje()} />
          <Campo formulario={formulario} nome="valor" rotulo="Valor (R$)" inputMode="decimal" placeholder="0,00" />
          <Campo formulario={formulario} nome="observacao" rotulo="Observação" conferir={false} placeholder="Ata, recibo" />
          <Button type="submit" disabled={salvar.isPending || !socioId}>{retirada ? 'Salvar retirada' : 'Lançar retirada'}</Button>
        </form>
      </DialogContent>
    </Dialog>
  )
}

/**
 * Sócios do contrato social e as retiradas do mês. O parceiro comercial que
 * não está no contrato fica na Equipe, com comissão.
 */
export function PainelDeSocios({ competencia }: { competencia: string | undefined }) {
  const socios = useSocios()
  const retiradas = useRetiradas(competencia)
  const excluirSocio = useExcluirSocio()
  const excluirRetirada = useExcluirRetirada()
  const [socio, setSocio] = useState<Socio | 'novo' | null>(null)
  const [retirada, setRetirada] = useState<Retirada | 'nova' | null>(null)
  const lista = socios.data ?? []
  const erro = excluirSocio.error ?? excluirRetirada.error

  return (
    <div className="grid gap-4 lg:grid-cols-2">
      {erro && <div className="lg:col-span-2"><Erro erro={mensagemDoErro(erro)} /></div>}
      <Card>
        <CardHeader>
          <CardTitle>Retiradas do mês</CardTitle>
          <CardAction><Button disabled={lista.length === 0} onClick={() => setRetirada('nova')}>Nova retirada</Button></CardAction>
        </CardHeader>
        <CardContent>
          <EstadoDaConsulta carregando={retiradas.isPending} erro={retiradas.error} vazio={retiradas.data?.length === 0}
            textoVazio={lista.length === 0 ? 'Cadastre os sócios do contrato para lançar retirada.' : 'Nenhuma retirada no mês.'} />
          {retiradas.data && retiradas.data.length > 0 && (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Sócio</TableHead>
                  <TableHead>Data</TableHead>
                  <TableHead className="text-right">Valor</TableHead>
                  <TableHead><span className="sr-only">Ações</span></TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {retiradas.data.map((r) => (
                  <TableRow key={r.id}>
                    <TableCell className="min-w-36 whitespace-normal">
                      <p className="font-medium">{r.socio}</p>
                      <p className="text-xs text-muted-foreground">{ROTULO_DA_RETIRADA[r.natureza]}{r.observacao ? ` - ${r.observacao}` : ''}</p>
                    </TableCell>
                    <TableCell>{formatarData(`${r.data}T12:00:00`)}</TableCell>
                    <TableCell className="text-right tabular-nums">{formatarCentavos(r.valorCents)}</TableCell>
                    <TableCell>
                      <AcoesDaLinha nome={`retirada de ${r.socio}`} principal={{ rotulo: 'Editar', aoClicar: () => setRetirada(r) }}
                        outras={[{ rotulo: 'Excluir', destrutiva: true, desabilitada: excluirRetirada.isPending, aoClicar: () => excluirRetirada.mutate(r.id) }]} />
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>
      <Card>
        <CardHeader>
          <CardTitle>Sócios do contrato</CardTitle>
          <CardAction><Button variant="outline" onClick={() => setSocio('novo')}>Cadastrar sócio</Button></CardAction>
        </CardHeader>
        <CardContent>
          <EstadoDaConsulta carregando={socios.isPending} erro={socios.error} vazio={lista.length === 0} textoVazio="Nenhum sócio cadastrado." />
          {lista.length > 0 && (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Sócio</TableHead>
                  <TableHead className="text-right">Retiradas</TableHead>
                  <TableHead><span className="sr-only">Ações</span></TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {lista.map((s) => (
                  <TableRow key={s.id}>
                    <TableCell className="min-w-36 whitespace-normal">
                      <p className="font-medium">{s.nome}</p>
                      {s.cpf && <p className="text-xs text-muted-foreground">CPF {mascaraCpf(s.cpf)}</p>}
                    </TableCell>
                    <TableCell className="text-right tabular-nums">{s.retiradas}</TableCell>
                    <TableCell>
                      <AcoesDaLinha nome={s.nome} principal={{ rotulo: 'Editar', aoClicar: () => setSocio(s) }}
                        outras={[{ rotulo: 'Excluir', destrutiva: true, desabilitada: excluirSocio.isPending, aoClicar: () => excluirSocio.mutate(s.id) }]} />
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>
      {socio && <FormularioDeSocio socio={socio === 'novo' ? null : socio} aoFechar={() => setSocio(null)} />}
      {retirada && <FormularioDeRetirada retirada={retirada === 'nova' ? null : retirada} socios={lista} aoFechar={() => setRetirada(null)} />}
    </div>
  )
}
