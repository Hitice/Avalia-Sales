import { ErroDaApi, novaChaveDeIdempotencia } from '@/api/cliente'

/**
 * Uma tentativa de cobrança: a mesma `Idempotency-Key` e o mesmo corpo (com o
 * mesmo token do cartão) até a API responder. Repetida depois de uma queda de
 * rede, a API reconhece a chave e devolve a venda já registrada, sem cobrar de novo.
 */
export interface Tentativa<T> {
  readonly assinatura: string
  readonly chave: string
  readonly corpo: T
}

/** A anterior, se o que está na tela não mudou; senão uma nova, com chave nova e corpo montado de novo. */
export async function tentativaPara<T>(anterior: Tentativa<T> | null, assinatura: string, montar: () => Promise<T>): Promise<Tentativa<T>> {
  if (anterior?.assinatura === assinatura) return anterior
  return { assinatura, chave: novaChaveDeIdempotencia(), corpo: await montar() }
}

/**
 * Depois de um erro, a tentativa continua só quando a API não deu a resposta
 * final: sem resposta (rede), 409 (a primeira ainda executa), 429 ou 5xx (a
 * API solta a chave). Com 4xx a resposta fica gravada na chave, e a correção
 * (outro cartão, outro dado) precisa de chave nova.
 */
export function tentativaContinua(erro: unknown): boolean {
  return !(erro instanceof ErroDaApi) || erro.status === 409 || erro.status === 429 || erro.status >= 500
}
