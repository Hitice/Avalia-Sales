import { zodResolver } from '@hookform/resolvers/zod'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { mensagemDoErro } from '@/api/cliente'
import { Erro } from '@/componentes/Tela'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldError, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatarCentavos, paraCentavos } from '@/dinheiro/dinheiro'
import { type Produto, useAlterarProduto, useAtivarProduto, useCadastrarProduto, useExcluirProduto, useProdutos } from './produtos'

const esquema = z.object({
  nome: z.string().trim().min(1, 'Digite o nome.').max(120, 'Use até 120 caracteres.'),
  preco: z.string().refine((texto) => paraCentavos(texto) !== null, 'Digite o preço, como 149,90.'),
})

type Formulario = z.infer<typeof esquema>

/** A tabela de preço do revendedor: a venda nunca sai abaixo dela. */
export function TelaDoCatalogo() {
  const produtos = useProdutos()
  const cadastrar = useCadastrarProduto()
  const alterar = useAlterarProduto()
  const ativar = useAtivarProduto()
  const excluir = useExcluirProduto()
  const [editando, setEditando] = useState<Produto | null>(null)
  const formulario = useForm<Formulario>({ resolver: zodResolver(esquema), defaultValues: { nome: '', preco: '' } })
  const { errors } = formulario.formState
  const erroDaLista = ativar.error ?? excluir.error

  function editar(produto: Produto | null) {
    setEditando(produto)
    formulario.reset(produto ? { nome: produto.nome, preco: (produto.precoCents / 100).toFixed(2).replace('.', ',') } : { nome: '', preco: '' })
  }

  async function salvar(dados: Formulario) {
    try {
      const produto = { nome: dados.nome, precoCents: paraCentavos(dados.preco)! }
      await (editando ? alterar.mutateAsync({ id: editando.id, ...produto }) : cadastrar.mutateAsync(produto))
      editar(null)
    } catch (erro) {
      formulario.setError('root', { message: mensagemDoErro(erro) })
    }
  }

  return (
    <div className="grid gap-4 lg:grid-cols-[1fr_20rem]">
      <Card>
        <CardHeader>
          <CardTitle>Catálogo</CardTitle>
        </CardHeader>
        <CardContent className="grid gap-3">
          <Erro erro={erroDaLista ? mensagemDoErro(erroDaLista) : null} />
          {produtos.isPending && <p className="text-sm text-muted-foreground">Carregando...</p>}
          {produtos.isError && (
            <Alert variant="destructive"><AlertDescription>{produtos.error.message}</AlertDescription></Alert>
          )}
          {produtos.data?.length === 0 && (
            <p className="text-sm text-muted-foreground">Nenhum produto ainda. Cadastre o primeiro em Novo produto.</p>
          )}
          {produtos.data && produtos.data.length > 0 && (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Produto</TableHead>
                  <TableHead className="text-right">Preço</TableHead>
                  <TableHead><span className="sr-only">Ações</span></TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {produtos.data.map((produto) => (
                  <TableRow key={produto.id}>
                    <TableCell>{produto.nome} {!produto.ativo && <Badge variant="outline">Fora de venda</Badge>}</TableCell>
                    <TableCell className="text-right tabular-nums">{formatarCentavos(produto.precoCents)}</TableCell>
                    <TableCell>
                      <div className="flex justify-end gap-2">
                        <Button variant="outline" onClick={() => editar(produto)}>Editar</Button>
                        <Button variant="outline" onClick={() => ativar.mutate({ id: produto.id, ativo: !produto.ativo })}>{produto.ativo ? 'Desativar' : 'Reativar'}</Button>
                        <Button variant="ghost" onClick={() => excluir.mutate(produto.id)}>Excluir</Button>
                      </div>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>

      <Card className="self-start">
        <CardHeader>
          <CardTitle>{editando ? `Editar ${editando.nome}` : 'Novo produto'}</CardTitle>
        </CardHeader>
        <CardContent>
          <form onSubmit={formulario.handleSubmit(salvar)} noValidate className="grid gap-4">
            {errors.root && <Alert variant="destructive"><AlertDescription>{errors.root.message}</AlertDescription></Alert>}
            <Field data-invalid={!!errors.nome}>
              <FieldLabel htmlFor="nome">Nome</FieldLabel>
              <Input id="nome" aria-invalid={!!errors.nome} {...formulario.register('nome')} />
              <FieldError errors={[errors.nome]} />
            </Field>
            <Field data-invalid={!!errors.preco}>
              <FieldLabel htmlFor="preco">Preço</FieldLabel>
              <Input id="preco" inputMode="decimal" placeholder="0,00" aria-invalid={!!errors.preco} {...formulario.register('preco')} />
              <FieldError errors={[errors.preco]} />
            </Field>
            <Button type="submit" disabled={cadastrar.isPending || alterar.isPending}>
              {editando ? 'Salvar produto' : cadastrar.isPending ? 'Cadastrando...' : 'Cadastrar produto'}
            </Button>
            {editando && <Button type="button" variant="ghost" onClick={() => editar(null)}>Cancelar</Button>}
          </form>
        </CardContent>
      </Card>
    </div>
  )
}
