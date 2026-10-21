import { zodResolver } from '@hookform/resolvers/zod'
import { useForm } from 'react-hook-form'
import { Navigate, useNavigate } from 'react-router'
import { z } from 'zod'
import { ErroDaApi, chamarApi } from '@/api/cliente'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { sessaoValida, useSessao } from './sessao'

const esquema = z.object({
  email: z.email('Digite um e-mail válido.'),
  senha: z.string().min(1, 'Digite a senha.'),
})

type Entrada = z.infer<typeof esquema>
type TokenEmitido = { token: string; expiraEm: string }

export function TelaDeEntrada() {
  const sessao = useSessao()
  const navegar = useNavigate()
  const formulario = useForm<Entrada>({ resolver: zodResolver(esquema), defaultValues: { email: '', senha: '' } })
  const { errors, isSubmitting } = formulario.formState

  if (sessaoValida(sessao)) {
    return <Navigate to="/painel" replace />
  }

  async function entrar(dados: Entrada) {
    try {
      const resposta = await chamarApi<TokenEmitido>('/auth/entrar', { method: 'POST', body: JSON.stringify(dados) })
      sessao.entrar(resposta.token, dados.email, resposta.expiraEm)
      navegar('/painel', { replace: true })
    } catch (erro) {
      const mensagem = erro instanceof ErroDaApi && erro.status === 401
        ? 'E-mail ou senha não conferem.'
        : erro instanceof ErroDaApi ? erro.message : 'Sem conexão com o servidor.'
      formulario.setError('root', { message: mensagem })
    }
  }

  return (
    <main className="flex min-h-svh flex-col items-center justify-center gap-6 p-4">
      <p className="text-lg font-semibold">Avalia Sales</p>
      <Card className="w-full max-w-sm">
        <CardHeader>
          <CardTitle className="text-xl">Entrar</CardTitle>
        </CardHeader>
        <CardContent>
          <form onSubmit={formulario.handleSubmit(entrar)} noValidate>
            <FieldGroup>
              {errors.root && (
                <Alert variant="destructive">
                  <AlertDescription>{errors.root.message}</AlertDescription>
                </Alert>
              )}
              <Field data-invalid={!!errors.email}>
                <FieldLabel htmlFor="email">E-mail</FieldLabel>
                <Input id="email" type="email" autoComplete="email" aria-invalid={!!errors.email} {...formulario.register('email')} />
                <FieldError errors={[errors.email]} />
              </Field>
              <Field data-invalid={!!errors.senha}>
                <FieldLabel htmlFor="senha">Senha</FieldLabel>
                <Input id="senha" type="password" autoComplete="current-password" aria-invalid={!!errors.senha} {...formulario.register('senha')} />
                <FieldError errors={[errors.senha]} />
              </Field>
              <Button type="submit" size="lg" disabled={isSubmitting}>
                {isSubmitting ? 'Entrando...' : 'Entrar'}
              </Button>
            </FieldGroup>
          </form>
        </CardContent>
      </Card>
    </main>
  )
}
