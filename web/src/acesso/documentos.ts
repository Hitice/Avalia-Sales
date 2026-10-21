/** A mesma conferência de dígito do CNPJ que a API faz, para o erro aparecer antes do envio. */
export function cnpjValido(texto: string): boolean {
  const d = texto.replace(/\D/g, '')
  if (d.length !== 14 || /^(\d)\1+$/.test(d)) return false
  const digito = (base: string, pesos: number[]) => {
    const resto = pesos.reduce((soma, peso, i) => soma + Number(base[i]) * peso, 0) % 11
    return resto < 2 ? 0 : 11 - resto
  }
  return digito(d, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]) === Number(d[12])
    && digito(d, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]) === Number(d[13])
}
