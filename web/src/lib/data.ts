/**
 * Datas de negócio são de Brasília (a competência e o fim do dia da API),
 * qualquer que seja o fuso do navegador. Brasília não tem horário de verão
 * desde 2019: o deslocamento é fixo em -03:00.
 */
export const FUSO_DE_BRASILIA = 'America/Sao_Paulo'

const diaDeBrasilia = new Intl.DateTimeFormat('en-CA', { timeZone: FUSO_DE_BRASILIA, year: 'numeric', month: '2-digit', day: '2-digit' })

/** "2026-10-31" vira o instante 23:59:59 daquele dia em Brasília, em UTC, como a API recebe (ela recusa deslocamento diferente de zero). */
export const fimDoDiaEmBrasilia = (dia: string) => new Date(`${dia}T23:59:59-03:00`).toISOString()

/** O dia (aaaa-mm-dd) em Brasília de um instante ISO: 2026-11-01T02:59:59Z é 2026-10-31. */
export const diaEmBrasilia = (iso: string) => diaDeBrasilia.format(new Date(iso))

/** O dia (aaaa-mm-dd) em Brasília de um instante em milissegundos: o "hoje" das telas, qualquer que seja o fuso do navegador. */
export const diaDeBrasiliaEm = (instante: number) => diaDeBrasilia.format(instante)

/** O instante (ms) da meia-noite de um dia (aaaa-mm-dd) em Brasília. */
export const inicioDoDiaEmBrasilia = (dia: string) => Date.parse(`${dia}T00:00:00-03:00`)
