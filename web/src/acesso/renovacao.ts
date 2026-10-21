import { type TokenEmitido, useSessao } from './sessao'

let emCurso: Promise<boolean> | null = null

/**
 * Troca o refresh do cookie por um token de acesso novo. Várias chamadas ao
 * mesmo tempo esperam a mesma renovação: o refresh é rotativo, e duas
 * renovações paralelas com o mesmo cookie encerrariam o login (RFC 9700).
 */
export function renovarSessao(): Promise<boolean> {
  emCurso ??= fetch('/api/auth/renovar', { method: 'POST', credentials: 'same-origin' })
    .then(async (resposta) => {
      if (!resposta.ok) {
        useSessao.getState().limpar()
        return false
      }

      useSessao.getState().entrar(await resposta.json() as TokenEmitido)
      return true
    })
    .catch(() => false)
    .finally(() => {
      emCurso = null
    })
  return emCurso
}

/** Ao abrir o app: tenta a sessão do cookie e marca que a tentativa terminou. */
export async function iniciarSessao(): Promise<void> {
  await renovarSessao()
  useSessao.getState().marcarIniciada()
}

export async function sairDaSessao(): Promise<void> {
  await fetch('/api/auth/sair', { method: 'POST', credentials: 'same-origin' }).catch(() => undefined)
  useSessao.getState().limpar()
}
