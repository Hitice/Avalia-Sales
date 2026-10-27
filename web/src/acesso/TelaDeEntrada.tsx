import { zodResolver } from '@hookform/resolvers/zod'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { Link, Navigate, useNavigate, useSearchParams } from 'react-router'
import { z } from 'zod'
import { ErroDaApi, chamarApi } from '@/api/cliente'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { PassoDoCodigo } from './PassoDoCodigo'
import { type SegundoFatorExigido, exigeCodigo } from './segundoFator'
import { type TokenEmitido, destinoDaVolta, inicioDoPapel, sessaoValida, useSessao } from './sessao'
import { MarcaAvalia } from '@/componentes/MarcaAvalia'
import { Rodape } from '@/componentes/Rodape'

const esquema = z.object({
  email: z.email('Digite um e-mail válido.'),
  senha: z.string().min(1, 'Digite a senha.'),
})

type Entrada = z.infer<typeof esquema>

export function TelaDeEntrada() {
  const sessao = useSessao()
  const navegar = useNavigate()
  const [parametros] = useSearchParams()
  const volta = destinoDaVolta(parametros.get('volta'))
  const formulario = useForm<Entrada>({ resolver: zodResolver(esquema), defaultValues: { email: '', senha: '' } })
  const { errors, isSubmitting } = formulario.formState
  const [desafio, setDesafio] = useState<SegundoFatorExigido | null>(null)

  if (sessaoValida(sessao)) {
    return <Navigate to={volta ?? inicioDoPapel(sessao.perfil?.papel)} replace />
  }

  function concluir(resposta: TokenEmitido) {
    sessao.entrar(resposta)
    navegar(volta ?? inicioDoPapel(resposta.perfil?.papel), { replace: true })
  }

  async function entrar(dados: Entrada) {
    try {
      const resposta = await chamarApi<TokenEmitido | SegundoFatorExigido>('/auth/entrar', { method: 'POST', body: JSON.stringify(dados) })
      if (exigeCodigo(resposta)) setDesafio(resposta)
      else concluir(resposta)
    } catch (erro) {
      const mensagem = erro instanceof ErroDaApi && erro.status === 401
        ? 'E-mail ou senha não conferem.'
        : erro instanceof ErroDaApi ? erro.message : 'Sem conexão com o servidor.'
      formulario.setError('root', { message: mensagem })
    }
  }

  return (
    <main className="flex min-h-svh flex-col items-center justify-center gap-6 p-4 pb-24">
      <MarcaAvalia />
      <Card className="relative w-full max-w-sm">
        <CardHeader>
          <CardTitle className="text-xl"><h1>Entrar</h1></CardTitle>
        </CardHeader>
        <CardContent>
          {desafio ? <PassoDoCodigo desafio={desafio} aoEntrar={concluir} /> : (
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
              <Link to="/esqueci" className="text-center text-sm text-muted-foreground underline underline-offset-4">Esqueci a senha</Link>
            </FieldGroup>
          </form>
          )}
        </CardContent>
      </Card>
      <p className="text-sm text-muted-foreground">
        Novo por aqui? <Link to="/cadastro" className="font-medium text-foreground underline underline-offset-4">Seja um revendedor</Link>
      </p>
      <Rodape />
    </main>
  )
}
