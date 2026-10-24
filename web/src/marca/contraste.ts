/** A mesma conta da API (Contraste.cs): luminância relativa da WCAG 2.2. */
export const BRANCO = '#FFFFFF'
export const ESCURO = '#120C2B'
/** O quase preto da escala neutra (tokens.css), para medir se a primária da revenda some nele. */
export const PRETO = '#18181B'

export const corValida = (cor: string) => /^#[0-9a-f]{6}$/i.test(cor)

function luminancia(cor: string): number {
  const canal = (i: number) => {
    const c = parseInt(cor.slice(i, i + 2), 16) / 255
    return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4
  }
  return 0.2126 * canal(1) + 0.7152 * canal(3) + 0.0722 * canal(5)
}

export function razao(a: string, b: string): number {
  const [claro, escuro] = [luminancia(a), luminancia(b)].sort((x, y) => y - x)
  return (claro + 0.05) / (escuro + 0.05)
}

export const textoSobre = (cor: string) => (razao(cor, BRANCO) >= razao(cor, ESCURO) ? BRANCO : ESCURO)

/** O que a API exige: texto legível na primária (4,5:1) e destaque visível no branco (3:1). */
export function problemasDaMarca(primaria: string, destaque: string): string[] {
  const problemas: string[] = []
  if (!corValida(primaria) || !corValida(destaque)) return ['Use a cor em hexadecimal, como #120C2B.']
  if (razao(primaria, textoSobre(primaria)) < 4.5) problemas.push('A cor primária não dá contraste com texto.')
  if (razao(destaque, BRANCO) < 3) problemas.push('A cor de destaque some no fundo branco.')
  return problemas
}
