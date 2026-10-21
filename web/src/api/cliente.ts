import { renovarSessao } from '@/acesso/renovacao'
import { useSessao } from '@/acesso/sessao'

/** Erro da API com a mensagem pronta para a tela: o detail do ProblemDetails, quando houver. */
export class ErroDaApi extends Error {
  readonly status: number

  constructor(status: number, mensagem: string) {
    super(mensagem)
    this.status = status
  }
}

const MENSAGEM_PADRAO: Record<number, string> = {
  401: 'Entre de novo para continuar.',
  403: 'Seu acesso não abre esta área.',
  429: 'Muitas tentativas. Espere um minuto e tente de novo.',
}

/** Renova um minuto antes de vencer, para a chamada não sair com token morto. */
const FOLGA_DA_RENOVACAO = 60_000

async function enviar(caminho: string, opcoes: RequestInit): Promise<Response> {
  const { token } = useSessao.getState()
  const cabecalhos = new Headers(opcoes.headers)
  if (opcoes.body !== undefined && !(opcoes.body instanceof FormData)) cabecalhos.set('Content-Type', 'application/json')
  if (token) cabecalhos.set('Authorization', `Bearer ${token}`)
  return fetch(`/api${caminho}`, { ...opcoes, headers: cabecalhos, credentials: 'same-origin' })
}

/**
 * Toda chamada passa por aqui: prefixo /api (o Nginx e o Vite levam à API),
 * token da sessão renovado perto de vencer, e um 401 tenta renovar uma vez
 * antes de encerrar a sessão.
 */
export async function chamarResposta(caminho: string, opcoes: RequestInit = {}): Promise<Response> {
  const { token, expiraEm } = useSessao.getState()
  if (token && expiraEm !== null && expiraEm - Date.now() < FOLGA_DA_RENOVACAO) {
    await renovarSessao()
  }

  let resposta = await enviar(caminho, opcoes)
  if (resposta.status === 401 && useSessao.getState().token && await renovarSessao()) {
    resposta = await enviar(caminho, opcoes)
  }

  if (resposta.status === 401 && useSessao.getState().token) useSessao.getState().limpar()

  if (!resposta.ok) {
    const corpo = await resposta.json().catch(() => null) as { detail?: string; title?: string } | null
    throw new ErroDaApi(resposta.status, corpo?.detail ?? MENSAGEM_PADRAO[resposta.status] ?? 'Não deu certo. Tente de novo.')
  }

  return resposta
}

export async function chamarApi<T>(caminho: string, opcoes: RequestInit = {}): Promise<T> {
  const resposta = await chamarResposta(caminho, opcoes)
  if (resposta.status === 204) return undefined as T
  const texto = await resposta.text()
  return (texto ? JSON.parse(texto) : undefined) as T
}

export const enviarJson = (metodo: 'POST' | 'PUT' | 'DELETE', corpo?: unknown): RequestInit => ({
  method: metodo,
  body: corpo === undefined ? undefined : JSON.stringify(corpo),
})

/** Mensagem para a tela a partir de qualquer erro de chamada. */
export function mensagemDoErro(erro: unknown): string {
  return erro instanceof ErroDaApi ? erro.message : 'Sem conexão com o servidor.'
}
