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
  429: 'Muitas tentativas. Espere um minuto e tente de novo.',
}

/**
 * Toda chamada passa por aqui: prefixo /api (o Nginx e o Vite levam à API),
 * token da sessão, e 401 encerra a sessão.
 */
export async function chamarApi<T>(caminho: string, opcoes: RequestInit = {}): Promise<T> {
  const { token, sair } = useSessao.getState()
  const cabecalhos = new Headers(opcoes.headers)
  if (opcoes.body !== undefined) cabecalhos.set('Content-Type', 'application/json')
  if (token) cabecalhos.set('Authorization', `Bearer ${token}`)

  const resposta = await fetch(`/api${caminho}`, { ...opcoes, headers: cabecalhos })
  if (resposta.status === 401 && token) sair()

  if (!resposta.ok) {
    const corpo = await resposta.json().catch(() => null) as { detail?: string; title?: string } | null
    throw new ErroDaApi(resposta.status, corpo?.detail ?? MENSAGEM_PADRAO[resposta.status] ?? 'Não deu certo. Tente de novo.')
  }

  return resposta.status === 204 ? (undefined as T) : (await resposta.json() as T)
}
