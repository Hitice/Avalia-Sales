/**
 * Copia para a área de transferência. Devolve falso quando o navegador recusa
 * (permissão negada, página sem HTTPS, onde navigator.clipboard nem existe):
 * quem chama avisa a pessoa em vez de fingir que copiou.
 */
export async function copiar(texto: string): Promise<boolean> {
  try {
    await navigator.clipboard.writeText(texto)
    return true
  } catch {
    return false
  }
}
