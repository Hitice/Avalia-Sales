import { zodResolver } from '@hookform/resolvers/zod'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { ErroDaApi, chamarApi, enviarJson } from '@/api/cliente'
import { Campo } from '@/componentes/Campo'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { FieldGroup } from '@/components/ui/field'
import { useEspera } from '@/lib/espera'
import type { SegundoFatorExigido } from './segundoFator'
import type { TokenEmitido } from './sessao'

const esquema = z.object({ codigo: z.string().trim().min(6, 'Digite o código de 6 dígitos ou um de recuperação.') })
type Codigo = z.infer<typeof esquema>

/** O segundo passo da entrada: o código do app ou do e-mail, ou um de recuperação. O desafio vale 5 minutos. */
export function PassoDoCodigo({ desafio, aoEntrar }: { desafio: SegundoFatorExigido; aoEntrar: (token: TokenEmitido) => void }) {
  const formulario = useForm<Codigo>({ resolver: zodResolver(esquema), defaultValues: { codigo: '' } })
  const { errors, isSubmitting } = formulario.formState
  const [reenviado, setReenviado] = useState(false)
  const espera = useEspera(60, desafio.meio === 'Email')
  const porEmail = desafio.meio === 'Email'

  async function reenviar() {
    try {
      await chamarApi('/auth/entrar/codigo/reenvio', enviarJson('POST', { desafio: desafio.desafio }))
      setReenviado(true)
      espera.iniciar()
    } catch (e) {
      formulario.setError('root', { message: e instanceof ErroDaApi ? e.message : 'Sem conexão com o servidor.' })
    }
  }

  async function confirmar({ codigo }: Codigo) {
    try {
      aoEntrar(await chamarApi<TokenEmitido>('/auth/entrar/codigo', enviarJson('POST', { desafio: desafio.desafio, codigo })))
    } catch (e) {
      formulario.setError('root', { message: e instanceof ErroDaApi ? e.message : 'Sem conexão com o servidor.' })
    }
  }

  return (
    <form onSubmit={formulario.handleSubmit(confirmar)} noValidate>
      <FieldGroup>
        {errors.root && <Alert variant="destructive"><AlertDescription>{errors.root.message}</AlertDescription></Alert>}
        <Campo formulario={formulario} nome="codigo" rotulo={porEmail ? `Código enviado para ${desafio.destino ?? 'seu e-mail'}` : 'Código do app autenticador'}
          inputMode="numeric" autoComplete="one-time-code" autoFocus conferir={false} />
        <Button type="submit" size="lg" disabled={isSubmitting}>{isSubmitting ? 'Conferindo...' : 'Entrar'}</Button>
        {porEmail && (
          <Button type="button" variant="ghost" onClick={reenviar} disabled={espera.esperando}>
            {espera.esperando ? `Reenviar em ${espera.restante} s` : reenviado ? 'Reenviar de novo' : 'Reenviar código'}
          </Button>
        )}
      </FieldGroup>
    </form>
  )
}
