import { zodResolver } from '@hookform/resolvers/zod'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { mensagemDoErro } from '@/api/cliente'
import type { Fornecedor, FuncaoDoFornecedor } from '@/api/tipos'
import { AcoesDaLinha } from '@/componentes/AcoesDaLinha'
import { Campo } from '@/componentes/Campo'
import { ListaPaginada } from '@/componentes/ListaPaginada'
import { Cabecalho, Erro, Rolagem } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldError, FieldLabel } from '@/components/ui/field'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { digitos, mascaraDocumento } from '@/lib/mascaras'
import { documentoOpcional, texto } from '@/lib/esquemas'
import { useAtivarFornecedor, useExcluirFornecedor, useFornecedores, useSalvarFornecedor } from './consultasDeFornecedores'

const FUNCOES: Record<FuncaoDoFornecedor, string> = {
  Grafica: 'Gráfica',
  Acrilico: 'Acrílico',
  Confeccao: 'Confecção com dado variável',
  Logistica: 'Logística',
  Outros: 'Outros',
}
const ORDEM = Object.keys(FUNCOES) as FuncaoDoFornecedor[]
const TODAS = 'todas'

const esquema = z.object({
  nome: texto('o nome'),
  documento: documentoOpcional,
  contato: z.string().trim().max(120, 'Use até 120 caracteres.'),
  observacao: z.string().trim().max(500, 'Use até 500 caracteres.'),
  funcoes: z.array(z.string()).min(1, 'Marque o que o fornecedor faz.'),
})
type Formulario = z.infer<typeof esquema>
const VAZIO: Formulario = { nome: '', documento: '', contato: '', observacao: '', funcoes: [] }

/** Fornecedores da operadora: gráfica, acrílico, confecção, logística. Com uso lançado, só se desativa. */
export function TelaDeFornecedores() {
  const [funcao, setFuncao] = useState<FuncaoDoFornecedor | undefined>(undefined)
  const fornecedores = useFornecedores(funcao)
  const salvar = useSalvarFornecedor()
  const ativar = useAtivarFornecedor()
  const excluir = useExcluirFornecedor()
  const [editando, setEditando] = useState<Fornecedor | null>(null)
  const formulario = useForm<Formulario>({ resolver: zodResolver(esquema), mode: 'onChange', defaultValues: VAZIO })
  const funcoes = formulario.watch('funcoes')
  const erro = salvar.error ?? ativar.error ?? excluir.error

  function editar(f: Fornecedor | null) {
    setEditando(f)
    formulario.reset(f
      ? { nome: f.nome, documento: f.documento ? mascaraDocumento(f.documento) : '', contato: f.contato ?? '', observacao: f.observacao ?? '', funcoes: f.funcoes }
      : VAZIO)
  }

  function marcar(f: FuncaoDoFornecedor, marcada: boolean) {
    const atuais = formulario.getValues('funcoes')
    formulario.setValue('funcoes', marcada ? [...atuais, f] : atuais.filter((x) => x !== f), { shouldValidate: true, shouldDirty: true, shouldTouch: true })
  }

  async function enviar(dados: Formulario) {
    try {
      await salvar.mutateAsync({
        id: editando?.id,
        nome: dados.nome,
        documento: digitos(dados.documento) || null,
        contato: dados.contato || null,
        funcoes: dados.funcoes as FuncaoDoFornecedor[],
        observacao: dados.observacao || null,
      })
      editar(null)
    } catch {
      // O erro aparece pelo estado da gravação.
    }
  }

  const erroDasFuncoes = formulario.formState.errors.funcoes
  const mostrarErroDasFuncoes = !!erroDasFuncoes && (formulario.formState.isSubmitted || formulario.formState.touchedFields.funcoes)

  return (
    <div className="grid grid-cols-1 gap-4 xl:lista-e-lateral-80">
      <div>
        <Cabecalho titulo="Fornecedores" />
        <Erro erro={erro ? mensagemDoErro(erro) : null} />
        <Card className="mt-3">
          <CardContent>
            <div className="mb-3 max-w-xs">
              <Select value={funcao ?? TODAS} onValueChange={(v) => setFuncao(v === TODAS ? undefined : (v as FuncaoDoFornecedor))}>
                <SelectTrigger aria-label="Filtrar por função" className="w-full"><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value={TODAS}>Todas as funções</SelectItem>
                  {ORDEM.map((f) => <SelectItem key={f} value={f}>{FUNCOES[f]}</SelectItem>)}
                </SelectContent>
              </Select>
            </div>
            <ListaPaginada lista={fornecedores} textoVazio="Nenhum fornecedor ainda." nomeDaBusca="Buscar nos fornecedores" placeholder="Nome, documento ou contato">
              {(itens) => (
                <Rolagem>
                  <Table semBusca>
                    <TableHeader>
                      <TableRow>
                        <TableHead>Fornecedor</TableHead>
                        <TableHead>Faz</TableHead>
                        <TableHead>Documento</TableHead>
                        <TableHead tipo="situacao">Situação</TableHead>
                        <TableHead tipo="numero">Usos</TableHead>
                        <TableHead tipo="acoes" />
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {itens.map((f) => (
                        <TableRow key={f.id}>
                          <TableCell className="min-w-40 whitespace-normal">
                            <p className="font-medium">{f.nome}</p>
                            {f.contato && <p className="text-xs text-muted-foreground">{f.contato}</p>}
                          </TableCell>
                          <TableCell className="whitespace-normal">
                            <div className="flex flex-wrap gap-1">
                              {f.funcoes.map((x) => <Badge key={x} variant="neutro">{FUNCOES[x as FuncaoDoFornecedor] ?? x}</Badge>)}
                            </div>
                          </TableCell>
                          <TableCell className="tabular-nums">{f.documento ? mascaraDocumento(f.documento) : '-'}</TableCell>
                          <TableCell><Badge variant={f.ativo ? 'sucesso' : 'neutro'}>{f.ativo ? 'Ativo' : 'Desativado'}</Badge></TableCell>
                          <TableCell>{f.usos}</TableCell>
                          <TableCell>
                            <AcoesDaLinha nome={f.nome} principal={{ rotulo: 'Editar', aoClicar: () => editar(f) }} outras={[
                              { rotulo: f.ativo ? 'Desativar' : 'Reativar', aoClicar: () => ativar.mutate({ id: f.id, ativo: !f.ativo }) },
                              { rotulo: 'Excluir', destrutiva: true, aoClicar: () => excluir.mutate(f.id) },
                            ]} />
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
      </div>
      <Card className="self-start">
        <CardHeader><CardTitle>{editando ? `Editar ${editando.nome}` : 'Novo fornecedor'}</CardTitle></CardHeader>
        <CardContent>
          <form className="grid gap-4" onSubmit={formulario.handleSubmit(enviar)} noValidate>
            <Campo formulario={formulario} nome="nome" rotulo="Nome" autoComplete="off" />
            <Campo formulario={formulario} nome="documento" rotulo="CNPJ ou CPF" inputMode="numeric" mascara={mascaraDocumento} conferir={false} />
            <Campo formulario={formulario} nome="contato" rotulo="Contato" autoComplete="off" conferir={false} />
            <Field data-invalid={mostrarErroDasFuncoes}>
              <FieldLabel>Faz</FieldLabel>
              <div className="grid gap-2" role="group" aria-label="Faz">
                {ORDEM.map((f) => (
                  <label key={f} className="flex min-h-11 items-center gap-2 text-sm md:min-h-0">
                    <input type="checkbox" className="size-4 accent-primary" checked={funcoes.includes(f)} onChange={(e) => marcar(f, e.target.checked)} />
                    {FUNCOES[f]}
                  </label>
                ))}
              </div>
              {mostrarErroDasFuncoes && <FieldError errors={[erroDasFuncoes]} />}
            </Field>
            <Campo formulario={formulario} nome="observacao" rotulo="Observação" multilinha conferir={false} />
            <Button type="submit" disabled={salvar.isPending}>{editando ? 'Salvar fornecedor' : 'Criar fornecedor'}</Button>
            {editando && <Button type="button" variant="ghost" onClick={() => editar(null)}>Cancelar</Button>}
          </form>
        </CardContent>
      </Card>
    </div>
  )
}
