import type { FieldValues, Path, PathValue, UseFormReturn } from 'react-hook-form'
import { chamarApi } from '@/api/cliente'
import type { EmpresaConsultada, EnderecoConsultado, EnderecoInformado } from '@/api/tipos'
import type { EnderecoDoFormulario } from '@/lib/esquemas'
import { digitos, mascaraCep } from '@/lib/mascaras'

export type ComEndereco = FieldValues & { endereco: EnderecoDoFormulario }

export const CAMPOS = ['cep', 'logradouro', 'numero', 'complemento', 'bairro', 'cidade', 'uf'] as const

/**
 * Preenche o endereço com o que a consulta trouxe, sem apagar o que a pessoa já
 * digitou: a consulta só completa campo vazio. Consulta que falha não faz nada,
 * e a pessoa digita.
 */
export function completar<T extends ComEndereco>(formulario: UseFormReturn<T>, achado: EnderecoConsultado) {
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
