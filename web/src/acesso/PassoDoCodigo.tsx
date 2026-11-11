import { zodResolver } from '@hookform/resolvers/zod'
import { useState } from 'react'
import { Controller, useForm } from 'react-hook-form'
import { z } from 'zod'
import { ErroDaApi, chamarApi, enviarJson } from '@/api/cliente'
import { Campo } from '@/componentes/Campo'
import { CodigoEmQuadrados } from '@/componentes/CodigoEmQuadrados'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Field, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field'
import { useEspera } from '@/lib/espera'
import type { SegundoFatorExigido } from './segundoFator'
import type { TokenEmitido } from './sessao'

const esquema = z.object({ codigo: z.string().trim().min(6, 'Digite o código de 6 dígitos ou um de recuperação.') })
type Codigo = z.infer<typeof esquema>

/**
 * O segundo passo da entrada: o código de 6 dígitos do app ou do e-mail, em
 * quadrados, que confere sozinho ao completar; ou, pelo botão, um código de
 * recuperação em campo de texto. O desafio vale 5 minutos.
 */
export function PassoDoCodigo({ desafio, aoEntrar }: { desafio: SegundoFatorExigido; aoEntrar: (token: TokenEmitido) => void }) {
  const formulario = useForm<Codigo>({ resolver: zodResolver(esquema), defaultValues: { codigo: '' } })
  const { errors, isSubmitting } = formulario.formState
  const [reenviado, setReenviado] = useState(false)
  const espera = useEspera(60, desafio.meio === 'Email')
  const porEmail = desafio.meio === 'Email'
  const [recuperacao, setRecuperacao] = useState(false)
  const rotulo = porEmail ? `Código enviado para ${desafio.destino ?? 'seu e-mail'}` : 'Código do app autenticador'

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
      // O código errado sai dos quadrados: o próximo já entra do primeiro.
      if (!recuperacao) formulario.setValue('codigo', '')
      formulario.setError('root', { message: e instanceof ErroDaApi ? e.message : 'Sem conexão com o servidor.' })
    }
  }

  return (
    <form onSubmit={formulario.handleSubmit(confirmar)} noValidate>
      <FieldGroup>
        {errors.root && <Alert variant="destructive"><AlertDescription>{errors.root.message}</AlertDescription></Alert>}
        {recuperacao ? (
          <Campo formulario={formulario} nome="codigo" rotulo="Código de recuperação" autoComplete="off" autoFocus conferir={false} />
        ) : (
          <Field data-invalid={!!errors.codigo}>
            <FieldLabel htmlFor="codigo">{rotulo}</FieldLabel>
            <Controller control={formulario.control} name="codigo" render={({ field }) => (
              <CodigoEmQuadrados id="codigo" rotulo={rotulo} valor={field.value} aoMudar={field.onChange} invalido={!!errors.codigo} desabilitado={isSubmitting}
                aoCompletar={() => void formulario.handleSubmit(confirmar)()} />
            )} />
            <FieldError errors={[errors.codigo]} />
          </Field>
        )}
        <Button type="submit" size="lg" disabled={isSubmitting}>{isSubmitting ? 'Conferindo...' : 'Verificar'}</Button>
        <Button type="button" variant="ghost" onClick={() => { setRecuperacao(!recuperacao); formulario.reset({ codigo: '' }) }}>
          {recuperacao ? 'Usar o código de 6 dígitos' : 'Usar código de recuperação'}
        </Button>
        {porEmail && (
          <Button type="button" variant="ghost" onClick={reenviar} disabled={espera.esperando}>
            {espera.esperando ? `Reenviar em ${espera.restante} s` : reenviado ? 'Reenviar de novo' : 'Reenviar código'}
          </Button>
        )}
      </FieldGroup>
    </form>
  )
}
