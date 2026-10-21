import { zodResolver } from '@hookform/resolvers/zod'
import { useForm } from 'react-hook-form'
import { Link, useNavigate } from 'react-router'
import { z } from 'zod'
import { chamarApi, mensagemDoErro } from '@/api/cliente'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { cnpjValido } from './documentos'
import { type TokenEmitido, useSessao } from './sessao'

const esquema = z.object({
  empresa: z.string().trim().min(1, 'Digite o nome da empresa.').max(120, 'Use até 120 caracteres.'),
  cnpj: z.string().refine(cnpjValido, 'Confira o CNPJ: são 14 números.'),
  nome: z.string().trim().min(1, 'Digite seu nome.').max(120, 'Use até 120 caracteres.'),
  email: z.email('Digite um e-mail válido.'),
  senha: z.string().min(10, 'A senha precisa ter 10 caracteres ou mais.'),
})

type Cadastro = z.infer<typeof esquema>

/** O cadastro da revenda: depois dele, o contrato chega por e-mail para assinar. */
export function TelaDeCadastro() {
  const entrar = useSessao((s) => s.entrar)
  const navegar = useNavigate()
  const formulario = useForm<Cadastro>({ resolver: zodResolver(esquema), defaultValues: { empresa: '', cnpj: '', nome: '', email: '', senha: '' } })
  const { errors, isSubmitting } = formulario.formState

  async function cadastrar(dados: Cadastro) {
    try {
      entrar(await chamarApi<TokenEmitido>('/cadastro', { method: 'POST', body: JSON.stringify(dados) }))
      navegar('/painel/inicio', { replace: true })
    } catch (erro) {
      formulario.setError('root', { message: mensagemDoErro(erro) })
    }
  }

  const campo = (nome: keyof Cadastro, rotulo: string, tipo = 'text', autoComplete?: string) => (
    <Field data-invalid={!!errors[nome]}>
      <FieldLabel htmlFor={nome}>{rotulo}</FieldLabel>
      <Input id={nome} type={tipo} autoComplete={autoComplete} aria-invalid={!!errors[nome]} {...formulario.register(nome)} />
      <FieldError errors={[errors[nome]]} />
    </Field>
  )

  return (
    <main className="flex min-h-svh flex-col items-center justify-center gap-6 p-4">
      <p className="text-lg font-semibold">Avalia Sales</p>
      <Card className="w-full max-w-md">
        <CardHeader>
          <CardTitle className="text-xl">Cadastrar revenda</CardTitle>
        </CardHeader>
        <CardContent>
          <form onSubmit={formulario.handleSubmit(cadastrar)} noValidate>
            <FieldGroup>
              {errors.root && <Alert variant="destructive"><AlertDescription>{errors.root.message}</AlertDescription></Alert>}
              {campo('empresa', 'Empresa', 'text', 'organization')}
              {campo('cnpj', 'CNPJ')}
              {campo('nome', 'Seu nome', 'text', 'name')}
              {campo('email', 'E-mail', 'email', 'email')}
              {campo('senha', 'Senha', 'password', 'new-password')}
              <Button type="submit" size="lg" disabled={isSubmitting}>{isSubmitting ? 'Cadastrando...' : 'Cadastrar'}</Button>
            </FieldGroup>
          </form>
        </CardContent>
      </Card>
      <p className="text-sm text-muted-foreground">
        Já tem cadastro? <Link to="/entrar" className="font-medium text-foreground underline underline-offset-4">Entrar</Link>
      </p>
    </main>
  )
}
