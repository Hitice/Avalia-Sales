import { type TokenEmitido, useSessao } from './sessao'

let emCurso: Promise<boolean> | null = null

const ESPERA_ANTES_DE_TENTAR_DE_NOVO = 1_500

/**
 * Uma tentativa. Só o 401 encerra a sessão: limite de pedidos (429), servidor
 * fora do ar ou rede caída não provam que o login acabou, e o cookie continua.
 */
async function tentar(): Promise<boolean | 'de-novo'> {
  try {
    const resposta = await fetch('/api/auth/renovar', { method: 'POST', credentials: 'same-origin' })
    if (resposta.ok) {
      useSessao.getState().entrar(await resposta.json() as TokenEmitido)
      return true
    }

    if (resposta.status === 401 || resposta.status === 403) {
      useSessao.getState().limpar()
      return false
    }

    return 'de-novo'
  } catch {
    return 'de-novo'
  }
}

/**
 * Troca o refresh do cookie por um token de acesso novo. Várias chamadas ao
 * mesmo tempo esperam a mesma renovação: o refresh é rotativo, e duas
 * renovações paralelas com o mesmo cookie encerrariam o login (RFC 9700).
 */
export function renovarSessao(): Promise<boolean> {
  emCurso ??= (async () => {
    const primeira = await tentar()
    if (primeira !== 'de-novo') return primeira
    await new Promise((pronto) => setTimeout(pronto, ESPERA_ANTES_DE_TENTAR_DE_NOVO))
    return (await tentar()) === true
  })().finally(() => {
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
