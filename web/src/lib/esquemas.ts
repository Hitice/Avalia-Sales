import { z } from 'zod'
import { documentoValido, emailValido, telefoneValido } from '@/acesso/documentos'
import { paraBps } from '@/api/rotulos'
import { paraCentavos } from '@/dinheiro/dinheiro'

/**
 * As regras de campo que a API também confere, num lugar só, para todo
 * formulário recusar igual e antes do envio. Campo opcional vazio passa.
 */
export const texto = (rotulo: string, maximo = 120) =>
  z.string().trim().min(1, `Digite ${rotulo}.`).max(maximo, `Use até ${maximo} caracteres.`)

export const email = z.string().trim().refine(emailValido, 'E-mail incompleto.')
export const emailOpcional = z.string().trim().refine((t) => !t || emailValido(t), 'E-mail incompleto.')
export const telefone = z.string().trim().refine(telefoneValido, 'DDD e número, como 34 99999-0000.')
export const telefoneOpcional = z.string().trim().refine((t) => !t || telefoneValido(t), 'DDD e número, como 34 99999-0000.')
export const documentoOpcional = z.string().trim().refine((t) => !t || documentoValido(t), 'CPF ou CNPJ com dígito errado.')

/** Senha por tamanho, como a API (NIST SP 800-63B): 10 caracteres ou mais, sem regra de tipo. */
export const SENHA_MINIMA = 10
export const senhaNova = z.string().min(SENHA_MINIMA, `${SENHA_MINIMA} caracteres ou mais.`)
export const senhaOpcional = z.string().refine((t) => !t || t.length >= SENHA_MINIMA, `${SENHA_MINIMA} caracteres ou mais, ou em branco.`)

export const reais = z.string().refine((t) => paraCentavos(t) !== null, 'Digite o valor, como 149,90.')
export const percentualOpcional = z.string().refine((t) => !t.trim() || paraBps(t) !== null, 'Digite em %, como 20 ou 22,5.')

/** O endereço da empresa, que vai no contrato e no distrato. A API confere as mesmas regras (Domain/Revendedores/Endereco). */
export const endereco = z.object({
  cep: z.string().refine((t) => t.replace(/\D/g, '').length === 8, 'CEP com 8 números.'),
  logradouro: texto('a rua ou avenida'),
  numero: texto('o número, ou S/N', 20),
  complemento: z.string().trim().max(60, 'Use até 60 caracteres.'),
  bairro: texto('o bairro', 80),
  cidade: texto('a cidade', 80),
  uf: z.string().trim().regex(/^[A-Za-z]{2}$/, 'UF com 2 letras.'),
})

export type EnderecoDoFormulario = z.infer<typeof endereco>

export const ENDERECO_VAZIO: EnderecoDoFormulario = { cep: '', logradouro: '', numero: '', complemento: '', bairro: '', cidade: '', uf: '' }
