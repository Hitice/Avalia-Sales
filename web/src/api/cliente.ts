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
  if (typeof opcoes.body === 'string' && !cabecalhos.has('Content-Type')) cabecalhos.set('Content-Type', 'application/json')
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
  let renovacaoIndisponivel = false
  if (resposta.status === 401 && useSessao.getState().token) {
    if (await renovarSessao()) {
      resposta = await enviar(caminho, opcoes)
      // Token novo e ainda 401: a sessão não vale mais.
      if (resposta.status === 401) useSessao.getState().limpar()
    } else {
      // Renovação recusada (cookie vencido) já limpou a sessão. Se só não deu para tentar (429, servidor fora, rede),
      // o cookie continua valendo: não derruba o login, só avisa.
      renovacaoIndisponivel = useSessao.getState().token !== null
    }
  }

  if (!resposta.ok) {
    const corpo = await resposta.json().catch(() => null) as { detail?: string; title?: string } | null
    const padrao = renovacaoIndisponivel ? 'Não deu para renovar o acesso agora. Tente de novo.' : MENSAGEM_PADRAO[resposta.status]
    throw new ErroDaApi(resposta.status, corpo?.detail ?? padrao ?? 'Não deu certo. Tente de novo.')
  }

  return resposta
}

async function executar<T>(caminho: string, opcoes: RequestInit): Promise<T> {
  const resposta = await chamarResposta(caminho, opcoes)
  if (resposta.status === 204) return undefined as T
  const texto = await resposta.text()
  return (texto ? JSON.parse(texto) : undefined) as T
}

/** Gravações em andamento, por método, caminho e corpo. */
const emAndamento = new Map<string, Promise<unknown>>()

/**
 * Gravação idêntica enquanto a primeira não voltou (toque duplo, Enter repetido)
 * vai uma vez só: as duas chamadas recebem a mesma resposta. O servidor não
 * tem chave de idempotência nas gravações, então sem isto o segundo toque
 * criava outra venda, despesa ou pedido. Consulta (GET) nunca é unida.
 */
export function chamarApi<T>(caminho: string, opcoes: RequestInit = {}): Promise<T> {
  const metodo = (opcoes.method ?? 'GET').toUpperCase()
  if (metodo === 'GET' || (opcoes.body !== undefined && typeof opcoes.body !== 'string')) return executar<T>(caminho, opcoes)

  const chave = `${metodo} ${caminho} ${opcoes.body ?? ''}`
  const igual = emAndamento.get(chave)
  if (igual) return igual as Promise<T>

  const chamada = executar<T>(caminho, opcoes).finally(() => emAndamento.delete(chave))
  emAndamento.set(chave, chamada)
  return chamada
}

export const enviarJson = (metodo: 'POST' | 'PUT' | 'DELETE', corpo?: unknown): RequestInit => ({
  method: metodo,
  body: corpo === undefined ? undefined : JSON.stringify(corpo),
})

/** Mensagem para a tela a partir de qualquer erro de chamada. */
export function mensagemDoErro(erro: unknown): string {
  return erro instanceof ErroDaApi ? erro.message : 'Sem conexão com o servidor.'
}
