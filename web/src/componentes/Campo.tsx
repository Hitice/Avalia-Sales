import { Check } from 'lucide-react'
import type { ComponentProps } from 'react'
import type { FieldPath, FieldValues, UseFormReturn } from 'react-hook-form'
import { Field, FieldError, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import { cn } from '@/lib/utils'

type Props<T extends FieldValues> = {
  formulario: UseFormReturn<T>
  nome: FieldPath<T>
  rotulo: string
  /** Fica verde quando o dado confere; para campo sem regra além de existir, deixe falso. */
  conferir?: boolean
  multilinha?: boolean
  /** Reescreve o texto a cada tecla (lib/mascaras): só dígitos, com a pontuação do dado. */
  mascara?: (texto: string) => string
} & Omit<ComponentProps<typeof Input>, 'name' | 'id'>

/**
 * Campo do formulário com o rótulo, o erro embaixo e o aria-invalid. O erro
 * aparece ao sair do campo ou ao enviar; o verde aparece assim que o dado
 * confere. Com o formulário em mode 'onChange', o verde acompanha a digitação.
 * O verde vale também com o campo em foco, por cima do traço da marca.
 */
export function Campo<T extends FieldValues>({ formulario, nome, rotulo, conferir = true, multilinha = false, mascara, className, ...extra }: Props<T>) {
  const estado = formulario.getFieldState(nome, formulario.formState)
  const valor = formulario.watch(nome) as unknown
  const preenchido = typeof valor === 'string' ? valor.trim().length > 0 : valor !== undefined && valor !== null
  const mostrarErro = !!estado.error && (estado.isTouched || formulario.formState.isSubmitted)
  const valido = conferir && preenchido && !estado.error && estado.isDirty
  const id = String(nome)
  const registro = formulario.register(nome)
  const aoDigitar: typeof registro.onChange = (evento) => {
    if (mascara) evento.target.value = mascara(evento.target.value)
    return registro.onChange(evento)
  }

  return (
    <Field data-invalid={mostrarErro}>
      <FieldLabel htmlFor={id}>{rotulo}</FieldLabel>
      <div className="relative">
        {multilinha
          ? <Textarea id={id} aria-invalid={mostrarErro} data-valido={valido || undefined} className={className} {...registro} />
          : <Input id={id} aria-invalid={mostrarErro} data-valido={valido || undefined} className={cn(valido && 'pr-9', className)} {...extra} {...registro} onChange={aoDigitar} />}
        {valido && !multilinha && <Check aria-hidden className="pointer-events-none absolute top-1/2 right-3 size-4 -translate-y-1/2 text-sucesso" />}
      </div>
      {mostrarErro && <FieldError errors={[estado.error]} />}
    </Field>
  )
}
