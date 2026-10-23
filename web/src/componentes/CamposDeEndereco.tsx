import type { FieldPath, UseFormReturn } from 'react-hook-form'
import { chamarApi } from '@/api/cliente'
import type { EnderecoConsultado } from '@/api/tipos'
import { Campo } from '@/componentes/Campo'
import { CAMPOS, type ComEndereco, completar } from '@/lib/endereco'
import { digitos, mascaraCep } from '@/lib/mascaras'

/** Os campos do endereço. O CEP completo busca rua, bairro, cidade e UF. */
export function CamposDeEndereco<T extends ComEndereco>({ formulario }: { formulario: UseFormReturn<T> }) {
  const nome = (campo: (typeof CAMPOS)[number]) => `endereco.${campo}` as FieldPath<T>

  async function buscarCep(texto: string) {
    const cep = digitos(texto, 8)
    if (cep.length !== 8) return
    try {
      completar(formulario, await chamarApi<EnderecoConsultado>(`/cadastro/cep/${cep}`))
    } catch {
      // Sem resultado, a pessoa digita.
    }
  }

  return (
    <>
      <Campo formulario={formulario} nome={nome('cep')} rotulo="CEP" inputMode="numeric" autoComplete="postal-code" placeholder="00000-000"
        mascara={(t) => { void buscarCep(t); return mascaraCep(t) }} />
      <Campo formulario={formulario} nome={nome('logradouro')} rotulo="Rua ou avenida" autoComplete="address-line1" />
      <div className="grid grid-cols-2 gap-4">
        <Campo formulario={formulario} nome={nome('numero')} rotulo="Número" conferir={false} />
        <Campo formulario={formulario} nome={nome('complemento')} rotulo="Complemento" conferir={false} autoComplete="address-line2" />
      </div>
      <Campo formulario={formulario} nome={nome('bairro')} rotulo="Bairro" />
      <div className="grid grid-cols-4 gap-4">
        <div className="col-span-3"><Campo formulario={formulario} nome={nome('cidade')} rotulo="Cidade" autoComplete="address-level2" /></div>
        <Campo formulario={formulario} nome={nome('uf')} rotulo="UF" maxLength={2} autoComplete="address-level1" mascara={(t) => t.replace(/[^A-Za-z]/g, '').toUpperCase()} />
      </div>
    </>
  )
}
