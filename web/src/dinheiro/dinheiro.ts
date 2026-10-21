/**
 * Dinheiro trafega em centavos inteiros, como na API. O texto digitado vira
 * centavos sem passar por número de ponto flutuante: "149,90" é 14990, e não
 * 149.9 * 100.
 */
const formato = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' })

export function formatarCentavos(centavos: number): string {
  return formato.format(centavos / 100)
}

/** Aceita "149,90", "1.234,56", "149.90" e "149". Devolve null se não for um valor válido. */
export function paraCentavos(texto: string): number | null {
  const limpo = texto.replace(/\s|R\$/g, '')
  if (!/^\d[\d.,]*$/.test(limpo)) {
    return null
  }

  let inteiro: string
  let fracao = ''
  if (limpo.includes(',')) {
    const partes = limpo.split(',')
    if (partes.length !== 2) return null
    inteiro = partes[0].replace(/\./g, '')
    fracao = partes[1]
  } else {
    const ultimoPonto = limpo.lastIndexOf('.')
    const depois = ultimoPonto >= 0 ? limpo.length - ultimoPonto - 1 : 0
    if (ultimoPonto >= 0 && depois <= 2 && limpo.indexOf('.') === ultimoPonto) {
      inteiro = limpo.slice(0, ultimoPonto)
      fracao = limpo.slice(ultimoPonto + 1)
    } else {
      inteiro = limpo.replace(/\./g, '')
    }
  }

  if (!/^\d+$/.test(inteiro) || !/^\d{0,2}$/.test(fracao)) {
    return null
  }

  const centavos = Number(inteiro) * 100 + Number(fracao.padEnd(2, '0'))
  return Number.isSafeInteger(centavos) ? centavos : null
}
