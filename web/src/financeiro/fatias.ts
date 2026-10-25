/** Uma parte do gráfico de pizza, em centavos. */
export type Fatia = { nome: string; valorCents: number }

/**
 * As partes de uma pizza que se lê: no máximo quatro, da maior para a menor,
 * e o que passar junta em "Outros". Parte zerada ou negativa (estorno maior
 * que a saída do mês) não entra: pizza só mostra o que saiu.
 */
export function fatias(itens: Fatia[], maximo = 4): Fatia[] {
  const positivas = itens.filter((i) => i.valorCents > 0).sort((a, b) => b.valorCents - a.valorCents)
  if (positivas.length <= maximo) return positivas
  const resto = positivas.slice(maximo - 1).reduce((soma, i) => soma + i.valorCents, 0)
  return [...positivas.slice(0, maximo - 1), { nome: 'Outros', valorCents: resto }]
}

/** "2026-10" vira "outubro de 2026". */
export function nomeDoMes(competencia: string): string {
  const [ano, mes] = competencia.split('-').map(Number)
  return new Date(ano, mes - 1, 1).toLocaleDateString('pt-BR', { month: 'long', year: 'numeric' })
}

/** "2026-10" vira "out". */
export function mesCurto(competencia: string): string {
  const [ano, mes] = competencia.split('-').map(Number)
  return new Date(ano, mes - 1, 1).toLocaleDateString('pt-BR', { month: 'short' }).replace('.', '')
}

/** O mês corrente em Brasília, o mesmo da competência da API. */
export function mesCorrente(agora = new Date()): string {
  const partes = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Sao_Paulo', year: 'numeric', month: '2-digit' }).formatToParts(agora)
  return `${partes.find((p) => p.type === 'year')?.value}-${partes.find((p) => p.type === 'month')?.value}`
}

/** Hoje em Brasília, como o campo de data quer (2026-10-24). */
export function hoje(agora = new Date()): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Sao_Paulo', year: 'numeric', month: '2-digit', day: '2-digit' }).format(agora)
}

/** O percentual inteiro de uma parte, para o rótulo direto da pizza. */
export function percentual(parte: number, total: number): string {
  return total > 0 ? `${Math.round((parte * 100) / total)}%` : '0%'
}

/** O mês a n meses de distância: mesSomado("2026-12", 1) é "2027-01". */
export function mesSomado(competencia: string, n: number): string {
  const [ano, mes] = competencia.split('-').map(Number)
  const data = new Date(ano, mes - 1 + n, 1)
  return `${data.getFullYear()}-${String(data.getMonth() + 1).padStart(2, '0')}`
}
