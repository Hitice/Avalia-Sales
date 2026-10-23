import type { FieldPath, FieldValues, Path, PathValue, UseFormReturn } from 'react-hook-form'
import { chamarApi } from '@/api/cliente'
import type { EmpresaConsultada, EnderecoConsultado, EnderecoInformado } from '@/api/tipos'
import { Campo } from '@/componentes/Campo'
import type { EnderecoDoFormulario } from '@/lib/esquemas'
import { digitos, mascaraCep } from '@/lib/mascaras'

type ComEndereco = FieldValues & { endereco: EnderecoDoFormulario }

const CAMPOS = ['cep', 'logradouro', 'numero', 'complemento', 'bairro', 'cidade', 'uf'] as const

/**
 * Preenche o endereço com o que a consulta trouxe, sem apagar o que a pessoa já
 * digitou: a consulta só completa campo vazio. Consulta que falha não faz nada,
 * e a pessoa digita.
 */
function completar<T extends ComEndereco>(formulario: UseFormReturn<T>, achado: EnderecoConsultado) {
  for (const campo of CAMPOS) {
    const valor = campo === 'cep' && achado.cep ? mascaraCep(achado.cep) : achado[campo]
    const caminho = `endereco.${campo}` as Path<T>
    if (!valor || String(formulario.getValues(caminho) ?? '').trim()) continue
    formulario.setValue(caminho, valor as PathValue<T, Path<T>>, { shouldDirty: true, shouldValidate: true })
  }
}

/** A razão social e o endereço pelo CNPJ (BrasilAPI, pela nossa API). Nulo se não achou. */
export async function consultarCnpj(cnpj: string): Promise<EmpresaConsultada | null> {
  try {
    return await chamarApi<EmpresaConsultada>(`/cadastro/cnpj/${digitos(cnpj)}`)
  } catch {
    return null
  }
}

/** Depois do CNPJ válido: completa o endereço que estiver vazio. */
export function completarPeloCnpj<T extends ComEndereco>(formulario: UseFormReturn<T>, empresa: EmpresaConsultada) {
  completar(formulario, empresa.endereco)
}

/** Para a API: só os dígitos do CEP, UF em maiúsculas e complemento vazio como nulo. */
export const paraEnderecoInformado = (e: EnderecoDoFormulario): EnderecoInformado => ({
  ...e,
  cep: digitos(e.cep),
  uf: e.uf.trim().toUpperCase(),
  complemento: e.complemento.trim() || null,
})

export const doEnderecoInformado = (e: EnderecoInformado | null | undefined): EnderecoDoFormulario | undefined =>
  e ? { ...e, cep: mascaraCep(e.cep), complemento: e.complemento ?? '' } : undefined

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
