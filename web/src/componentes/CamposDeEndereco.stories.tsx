import { zodResolver } from '@hookform/resolvers/zod'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { useEffect } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { ENDERECO_VAZIO, endereco, type EnderecoDoFormulario } from '@/lib/esquemas'
import { CamposDeEndereco } from './CamposDeEndereco'

const esquema = z.object({ endereco })

/** O endereço vazio, preenchido pela consulta (verde) e com o número esquecido depois de enviar. */
function Exemplo({ preenchido, enviado }: { preenchido: Partial<EnderecoDoFormulario>; enviado: boolean }) {
  const formulario = useForm<z.infer<typeof esquema>>({ resolver: zodResolver(esquema), mode: 'onChange', defaultValues: { endereco: ENDERECO_VAZIO } })
  useEffect(() => {
    for (const [campo, valor] of Object.entries(preenchido)) {
      formulario.setValue(`endereco.${campo as keyof EnderecoDoFormulario}`, valor, { shouldDirty: true, shouldValidate: true })
    }
    if (enviado) void formulario.handleSubmit(() => undefined)()
  }, [formulario, preenchido, enviado])
  return <div className="grid max-w-md gap-4"><CamposDeEndereco formulario={formulario} /></div>
}

const consultado = { cep: '38400-192', logradouro: 'Avenida Princesa Izabel', bairro: 'Fundinho', cidade: 'Uberlândia', uf: 'MG' }

const meta = { title: 'Formulário/Endereço', component: Exemplo, args: { preenchido: {}, enviado: false } } satisfies Meta<typeof Exemplo>
export default meta
type Historia = StoryObj<typeof meta>

export const Vazio: Historia = {}
export const PreenchidoPelaConsulta: Historia = { args: { preenchido: { ...consultado, numero: '1331', complemento: 'Casa 1' } } }
export const FaltandoONumero: Historia = { args: { preenchido: consultado, enviado: true } }
