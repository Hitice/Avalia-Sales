/** Uma coluna do CSV: o título no cabeçalho e o valor de cada linha, já como a tela mostra. */
export type ColunaDoCsv<T> = { titulo: string; valor: (item: T) => string | number | null | undefined }

/** O que a lista exporta: o nome do arquivo (sem extensão) e as colunas. */
export type Exportacao<T> = { arquivo: string; colunas: ColunaDoCsv<T>[] }

/** No máximo isto de linhas num arquivo: 100 páginas de 100, para a exportação não varrer a API sem fim. */
export const LINHAS_MAXIMAS = 10_000

/** Centavos como o Excel em português lê: 1234,56, sem o "R$" e sem ponto de milhar. */
export const centavosNoCsv = (cents: number) => (cents / 100).toFixed(2).replace('.', ',')

/** Data ISO (2026-10-31 ou com hora) como 31/10/2026, ou com a hora de Brasília quando vem com hora. */
export function dataNoCsv(valor: string | null | undefined, comHora = false): string {
  if (!valor) return ''
  const data = new Date(valor.length === 10 ? `${valor}T12:00:00` : valor)
  return comHora
    ? data.toLocaleString('pt-BR', { timeZone: 'America/Sao_Paulo', day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' })
    : data.toLocaleDateString('pt-BR', { timeZone: 'America/Sao_Paulo' })
}

const celula = (valor: string | number | null | undefined) => {
  const texto = valor === null || valor === undefined ? '' : String(valor)
  // Texto que começa com = + - @ vira fórmula na planilha: o apóstrofo o mantém como texto (injeção de CSV).
  const seguro = /^[=+\-@\t\r]/.test(texto) && !/^-?\d+(,\d+)?$/.test(texto) ? `'${texto}` : texto
  return /[;"\n\r]/.test(seguro) ? `"${seguro.replaceAll('"', '""')}"` : seguro
}

/** O CSV com ponto e vírgula, como o Excel em português abre com um clique. */
export function paraCsv<T>(itens: T[], colunas: ColunaDoCsv<T>[]): string {
  const linhas = [colunas.map((c) => celula(c.titulo)), ...itens.map((item) => colunas.map((c) => celula(c.valor(item))))]
  return linhas.map((l) => l.join(';')).join('\r\n')
}

/** Baixa o texto como arquivo .csv; o BOM faz o Excel ler os acentos em UTF-8. */
export function baixarCsv(arquivo: string, texto: string) {
  const endereco = URL.createObjectURL(new Blob(['﻿', texto], { type: 'text/csv;charset=utf-8' }))
  const link = document.createElement('a')
  link.href = endereco
  link.download = `${arquivo}-${new Date().toISOString().slice(0, 10)}.csv`
  link.click()
  URL.revokeObjectURL(endereco)
}
