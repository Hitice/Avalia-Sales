import type { FieldValues, Path, PathValue, UseFormReturn } from 'react-hook-form'
import { chamarApi } from '@/api/cliente'
import type { EmpresaConsultada, EnderecoConsultado, EnderecoInformado } from '@/api/tipos'
import type { EnderecoDoFormulario } from '@/lib/esquemas'
import { digitos, mascaraCep } from '@/lib/mascaras'

export type ComEndereco = FieldValues & { endereco: EnderecoDoFormulario }

export const CAMPOS = ['cep', 'logradouro', 'numero', 'complemento', 'bairro', 'cidade', 'uf'] as const

/** O que a consulta já pôs em cada campo: se o campo ainda tem esse valor, a pessoa não mexeu e a consulta seguinte pode trocá-lo. */
export type Automaticos = Partial<Record<(typeof CAMPOS)[number], string>>

/**
 * Preenche o endereço com o que a consulta trouxe, sem apagar o que a pessoa
 * digitou: a consulta completa campo vazio e troca o que a consulta anterior
 * pôs e a pessoa não tocou (CEP corrigido). Consulta que falha não faz nada,
 * e a pessoa digita. Devolve o que ficou preenchido pela consulta, para a
 * próxima.
 */
export function completar<T extends ComEndereco>(formulario: UseFormReturn<T>, achado: EnderecoConsultado, anteriores: Automaticos = {}): Automaticos {
  const automaticos: Automaticos = { ...anteriores }
  for (const campo of CAMPOS) {
    const valor = campo === 'cep' && achado.cep ? mascaraCep(achado.cep) : achado[campo]
    const caminho = `endereco.${campo}` as Path<T>
    const atual = String(formulario.getValues(caminho) ?? '').trim()
    if (!valor || (atual && atual !== anteriores[campo])) continue
    formulario.setValue(caminho, valor as PathValue<T, Path<T>>, { shouldDirty: true, shouldValidate: true })
    automaticos[campo] = String(valor)
  }
  return automaticos
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
