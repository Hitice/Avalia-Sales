/**
 * Máscaras de digitação: o campo aceita só os dígitos, até o tamanho do dado, e
 * mostra a pontuação enquanto a pessoa digita. Para a API vão só os dígitos
 * (`digitos`), que é como ela grava e confere.
 */
export const digitos = (texto: string, maximo = Infinity) => texto.replace(/\D/g, '').slice(0, maximo)

function aplicar(texto: string, molde: string): string {
  let saida = ''
  let i = 0
  for (const letra of molde) {
    if (i >= texto.length) break
    if (letra === '0') saida += texto[i++]
    else saida += letra
  }
  return saida
}

export const mascaraCnpj = (texto: string) => aplicar(digitos(texto, 14), '00.000.000/0000-00')

export const mascaraCpf = (texto: string) => aplicar(digitos(texto, 11), '000.000.000-00')

/** Até 11 dígitos é CPF; do 12º em diante vira CNPJ. */
export const mascaraDocumento = (texto: string) => (digitos(texto).length > 11 ? mascaraCnpj(texto) : mascaraCpf(texto))

/** Fixo (10 dígitos) ou celular (11). */
export const mascaraTelefone = (texto: string) => {
  const d = digitos(texto, 11)
  return aplicar(d, d.length > 10 ? '(00) 00000-0000' : '(00) 0000-0000')
}

export const mascaraCodigo = (texto: string) => digitos(texto, 6)
