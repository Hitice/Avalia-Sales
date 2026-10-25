const digito = (base: string, pesos: number[]) => {
  const resto = pesos.reduce((soma, peso, i) => soma + Number(base[i]) * peso, 0) % 11
  return resto < 2 ? 0 : 11 - resto
}

/** A mesma conferência de dígito do CNPJ que a API faz, para o erro aparecer antes do envio. */
export function cnpjValido(texto: string): boolean {
  const d = texto.replace(/\D/g, '')
  if (d.length !== 14 || /^(\d)\1+$/.test(d)) return false
  return digito(d, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]) === Number(d[12])
    && digito(d, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]) === Number(d[13])
}

export function cpfValido(texto: string): boolean {
  const d = texto.replace(/\D/g, '')
  if (d.length !== 11 || /^(\d)\1+$/.test(d)) return false
  return digito(d, [10, 9, 8, 7, 6, 5, 4, 3, 2]) === Number(d[9])
    && digito(d, [11, 10, 9, 8, 7, 6, 5, 4, 3, 2]) === Number(d[10])
}

export const documentoValido = (texto: string) => cpfValido(texto) || cnpjValido(texto)

/**
 * Forma, e não existência: quem confere a caixa é o e-mail que chega. O domínio
 * para no primeiro ponto depois do primeiro caractere: sem retrocesso, casa o
 * mesmo que [^\s@]+\.[^\s@]{2,}.
 */
export const emailValido = (texto: string) => /^[^\s@]+@[^\s@][^\s@.]*\.[^\s@]{2,}$/.test(texto.trim())

/** DDD mais 8 ou 9 dígitos. */
export const telefoneValido = (texto: string) => /^\d{10,11}$/.test(texto.replace(/\D/g, ''))
