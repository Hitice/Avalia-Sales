import { zodResolver } from '@hookform/resolvers/zod'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { ErroDaApi, chamarApi, enviarJson } from '@/api/cliente'
import { Campo } from '@/componentes/Campo'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { FieldGroup } from '@/components/ui/field'
import type { TokenEmitido } from './sessao'

const esquema = z.object({ codigo: z.string().trim().min(6, 'Digite o código de 6 dígitos ou um de recuperação.') })
type Codigo = z.infer<typeof esquema>

/** O segundo passo da entrada: o código do app autenticador, ou um de recuperação. O desafio vale 5 minutos. */
export function PassoDoCodigo({ desafio, aoEntrar }: { desafio: string; aoEntrar: (token: TokenEmitido) => void }) {
  const formulario = useForm<Codigo>({ resolver: zodResolver(esquema), defaultValues: { codigo: '' } })
  const { errors, isSubmitting } = formulario.formState

  async function confirmar({ codigo }: Codigo) {
    try {
      aoEntrar(await chamarApi<TokenEmitido>('/auth/entrar/codigo', enviarJson('POST', { desafio, codigo })))
    } catch (e) {
      formulario.setError('root', { message: e instanceof ErroDaApi ? e.message : 'Sem conexão com o servidor.' })
    }
  }

  return (
    <form onSubmit={formulario.handleSubmit(confirmar)} noValidate>
      <FieldGroup>
        {errors.root && <Alert variant="destructive"><AlertDescription>{errors.root.message}</AlertDescription></Alert>}
        <Campo formulario={formulario} nome="codigo" rotulo="Código do app autenticador" inputMode="numeric" autoComplete="one-time-code" autoFocus conferir={false} />
        <Button type="submit" size="lg" disabled={isSubmitting}>{isSubmitting ? 'Conferindo...' : 'Entrar'}</Button>
      </FieldGroup>
    </form>
  )
}
