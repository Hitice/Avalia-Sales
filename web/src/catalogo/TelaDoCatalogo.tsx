import { zodResolver } from '@hookform/resolvers/zod'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { ErroDaApi } from '@/api/cliente'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldError, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatarCentavos, paraCentavos } from '@/dinheiro/dinheiro'
import { useCadastrarProduto, useProdutos } from './produtos'

const esquema = z.object({
  nome: z.string().trim().min(1, 'Digite o nome.').max(120, 'Use até 120 caracteres.'),
  preco: z.string().refine((texto) => paraCentavos(texto) !== null, 'Digite o preço, como 149,90.'),
})

type Formulario = z.infer<typeof esquema>

export function TelaDoCatalogo() {
  const produtos = useProdutos()
  const cadastrar = useCadastrarProduto()
  const formulario = useForm<Formulario>({ resolver: zodResolver(esquema), defaultValues: { nome: '', preco: '' } })
  const { errors } = formulario.formState

  async function salvar(dados: Formulario) {
    try {
      await cadastrar.mutateAsync({ nome: dados.nome, precoCents: paraCentavos(dados.preco)! })
      formulario.reset()
    } catch (erro) {
      formulario.setError('root', { message: erro instanceof ErroDaApi ? erro.message : 'Sem conexão com o servidor.' })
    }
  }

  return (
    <div className="grid gap-4 lg:grid-cols-[1fr_20rem]">
      <Card>
        <CardHeader>
          <CardTitle>Catálogo</CardTitle>
        </CardHeader>
        <CardContent>
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
                </TableRow>
              </TableHeader>
              <TableBody>
                {produtos.data.map((produto) => (
                  <TableRow key={produto.id}>
                    <TableCell>{produto.nome}</TableCell>
                    <TableCell className="text-right tabular-nums">{formatarCentavos(produto.precoCents)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Novo produto</CardTitle>
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
            <Button type="submit" disabled={cadastrar.isPending}>
              {cadastrar.isPending ? 'Cadastrando...' : 'Cadastrar produto'}
            </Button>
          </form>
        </CardContent>
      </Card>
    </div>
  )
}
