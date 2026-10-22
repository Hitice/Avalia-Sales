import { zodResolver } from '@hookform/resolvers/zod'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { useEffect } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { email } from '@/lib/esquemas'
import { Campo } from './Campo'

const esquema = z.object({ email })

/** O campo nos três estados: vazio, conferido (verde) e recusado depois de sair dele. */
function Exemplo({ valor, sair }: { valor: string; sair: boolean }) {
  const formulario = useForm({ resolver: zodResolver(esquema), mode: 'onChange', defaultValues: { email: '' } })
  useEffect(() => {
    if (valor) formulario.setValue('email', valor, { shouldDirty: true, shouldValidate: true, shouldTouch: sair })
  }, [formulario, valor, sair])
  return <div className="max-w-sm"><Campo formulario={formulario} nome="email" rotulo="E-mail" type="email" /></div>
}

const meta = { title: 'Formulário/Campo', component: Exemplo, args: { valor: '', sair: false } } satisfies Meta<typeof Exemplo>
export default meta
type Historia = StoryObj<typeof meta>

export const Vazio: Historia = {}
export const Conferido: Historia = { args: { valor: 'marta@padaria.com.br' } }
export const Recusado: Historia = { args: { valor: 'marta@', sair: true } }
