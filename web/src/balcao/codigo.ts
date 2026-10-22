const ALFABETO = /^[0-9A-HJKMNP-TV-Z]{6}$/

/**
 * O código da placa a partir do que foi lido ou digitado: a URL do QR
 * (HTTPS://.../Q/K7M2PX) ou o código solto. Mesma normalização da
 * API: I e L viram 1, O vira 0, separadores somem. Vazio quando não é código.
 */
export function extrairCodigo(lido: string): string {
  const daUrl = /\/q\/([0-9a-z]{6})(?:[/?#]|$)/i.exec(lido.trim())
  const bruto = (daUrl ? daUrl[1] : lido).trim().toUpperCase().replace(/[\s\-._]/g, '').replace(/[IL]/g, '1').replace(/O/g, '0')
  return ALFABETO.test(bruto) ? bruto : ''
}
