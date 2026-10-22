import type { components } from '@/api/esquema'

export type ConfiguracaoDoCartao = components['schemas']['ConfiguracaoDoCartao']
export type DadosDoCartao = { numero: string; nome: string; validade: string; cvv: string }

/** Dígito verificador do cartão (Luhn): pega o número digitado errado antes de ir ao Pagar.me. */
export function cartaoValido(numero: string): boolean {
  const d = numero.replace(/\D/g, '')
  if (d.length < 13 || d.length > 19) return false
  let soma = 0
  for (let i = 0; i < d.length; i++) {
    let n = Number(d[d.length - 1 - i])
    if (i % 2 === 1) {
      n *= 2
      if (n > 9) n -= 9
    }
    soma += n
  }
  return soma % 10 === 0
}

/** MM/AA no futuro (vale até o fim do mês impresso). */
export function validadeValida(texto: string, agora = new Date()): boolean {
  const achado = /^(\d{2})\s*\/\s*(\d{2})$/.exec(texto.trim())
  if (!achado) return false
  const mes = Number(achado[1])
  const ano = 2000 + Number(achado[2])
  return mes >= 1 && mes <= 12 && new Date(ano, mes, 1) > agora
}

/**
 * O cartão vai do navegador direto ao Pagar.me (POST /tokens com a chave
 * pública) e volta como token de uso único: número e CVV nunca passam pela
 * nossa API. No simulador, o token é de mentira; final 0002 simula recusa.
 */
export async function tokenizarCartao(config: ConfiguracaoDoCartao, dados: DadosDoCartao): Promise<string> {
  const numero = dados.numero.replace(/\D/g, '')
  if (config.simulado) return `token_simulado_${numero.endsWith('0002') ? 'recusado_' : ''}${crypto.randomUUID()}`
  if (!config.chavePublica) throw new Error('Cartão indisponível nesta revenda.')

  const [mes, ano] = dados.validade.split('/').map((p) => Number(p.trim()))
  const resposta = await fetch(`https://api.pagar.me/core/v5/tokens?appId=${encodeURIComponent(config.chavePublica)}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ type: 'card', card: { number: numero, holder_name: dados.nome.trim(), exp_month: mes, exp_year: ano, cvv: dados.cvv.trim() } }),
  }).catch(() => null)
  const corpo = await resposta?.json().catch(() => null) as { id?: string } | null
  if (!resposta?.ok || !corpo?.id) throw new Error('O Pagar.me recusou os dados do cartão. Confira número, validade e código.')
  return corpo.id
}
