import { zodResolver } from '@hookform/resolvers/zod'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { chamarApi, enviarJson, mensagemDoErro } from '@/api/cliente'
import { Campo } from '@/componentes/Campo'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { FieldGroup } from '@/components/ui/field'
import { useEspera } from '@/lib/espera'
import { mascaraCodigo } from '@/lib/mascaras'
import { renovarSessao } from './renovacao'
import { useSessao } from './sessao'

const esquema = z.object({ codigo: z.string().regex(/^\d{6}$/, 'São 6 números.') })
type Codigo = z.infer<typeof esquema>

/**
 * Dono com e-mail ainda não confirmado: o painel fica só nesta tela. O e-mail
 * leva o código, digitado aqui, e o link, para quem abre em outro aparelho.
 * Confirmado, a sessão renovada traz o perfil novo e o painel abre.
 */
export function ConfirmeOEmail({ email }: { email: string }) {
  const formulario = useForm<Codigo>({ resolver: zodResolver(esquema), mode: 'onChange', defaultValues: { codigo: '' } })
  const { errors, isSubmitting } = formulario.formState
  const espera = useEspera(60, true)
  const [aviso, setAviso] = useState<string | null>(null)

  async function confirmar({ codigo }: Codigo) {
    try {
      await chamarApi('/conta/email/confirmacao/codigo', enviarJson('POST', { codigo }))
      await renovarSessao()
    } catch (erro) {
      formulario.setError('root', { message: mensagemDoErro(erro) })
    }
  }

  async function reenviar() {
    try {
      await chamarApi('/conta/email/confirmacao', enviarJson('POST'))
      espera.iniciar()
      setAviso('Código novo enviado.')
      formulario.clearErrors('root')
    } catch (erro) {
      formulario.setError('root', { message: mensagemDoErro(erro) })
    }
  }

  async function jaConfirmeiPeloLink() {
    await renovarSessao()
    if (!useSessao.getState().perfil?.emailConfirmado) formulario.setError('root', { message: 'Ainda não confirmado.' })
  }

  return (
    <div className="flex min-h-[70svh] items-center justify-center">
      <Card className="relative w-full max-w-md">
        <span aria-hidden className="traco absolute inset-x-0 top-0 h-1" />
        <CardHeader>
          <CardTitle className="text-xl"><h1>Confirme seu e-mail</h1></CardTitle>
          <p className="text-sm text-muted-foreground">Enviamos um código para <span className="font-medium text-foreground">{email}</span>.</p>
        </CardHeader>
        <CardContent>
          <form onSubmit={formulario.handleSubmit(confirmar)} noValidate>
            <FieldGroup>
              {errors.root && <Alert variant="destructive"><AlertDescription>{errors.root.message}</AlertDescription></Alert>}
              {aviso && !errors.root && <p role="status" className="text-sm">{aviso}</p>}
              <Campo formulario={formulario} nome="codigo" rotulo="Código" inputMode="numeric" autoComplete="one-time-code" autoFocus mascara={mascaraCodigo} />
              <Button type="submit" size="lg" disabled={isSubmitting}>{isSubmitting ? 'Conferindo...' : 'Confirmar'}</Button>
              <Button type="button" variant="outline" onClick={reenviar} disabled={espera.esperando}>
                {espera.esperando ? `Reenviar em ${espera.restante} s` : 'Reenviar código'}
              </Button>
              <Button type="button" variant="ghost" onClick={jaConfirmeiPeloLink}>Já confirmei pelo link</Button>
            </FieldGroup>
          </form>
        </CardContent>
      </Card>
    </div>
  )
}
