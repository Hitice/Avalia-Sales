const CHAVE = 'avalia:recarregado-em'

/** O navegador não achou o arquivo da tela: em geral, aba aberta antes de uma publicação nova. */
export const ehFalhaDeCarregamento = (erro: unknown) =>
  erro instanceof Error && /Importing a module script failed|Failed to fetch dynamically imported module|error loading dynamically imported module|Unable to preload CSS/i.test(erro.message)

/**
 * Depois de uma publicação, a aba aberta ainda tem o index.html antigo e pede
 * arquivos com o hash antigo, que não existem mais. Recarrega uma vez para pegar
 * a versão nova; se falhar de novo em menos de 1 minuto, não insiste e deixa a
 * tela de erro aparecer. Devolve se recarregou.
 */
export function recarregarParaVersaoNova(): boolean {
  try {
    const antes = Number(sessionStorage.getItem(CHAVE) ?? 0)
    if (Date.now() - antes < 60_000) return false
    sessionStorage.setItem(CHAVE, String(Date.now()))
  } catch {
    // Sem sessionStorage (aba privada restrita): recarrega mesmo assim, uma vez por erro.
  }

  window.location.reload()
  return true
}
