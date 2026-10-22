import { createHmac } from 'node:crypto'
import AxeBuilder from '@axe-core/playwright'
import { type Page, expect } from '@playwright/test'

/** Logins da demonstração (Demonstracao.cs). */
export const DONO = 'dono@revenda-a.dev'
const SENHA = 'demonstracao-123'

export async function entrar(page: Page, email = DONO, senha = SENHA) {
  await page.goto('/entrar')
  await page.getByLabel('E-mail').fill(email)
  await page.getByLabel('Senha').fill(senha)
  await page.getByRole('button', { name: 'Entrar' }).click()
  // A tela de entrada também tem h1: só vale quando saiu dela.
  await expect(page).not.toHaveURL(/\/entrar/)
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
}

/** Falha com violação séria ou crítica do axe (WCAG 2.2 AA), como pede o design system. */
export async function semViolacaoGrave(page: Page) {
  // Só com a tela pronta: no meio do carregamento o axe mede o que ainda está entrando.
  await page.getByRole('heading', { level: 1 }).first().waitFor()
  await page.waitForLoadState('networkidle')
  const resultado = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa']).analyze()
  const graves = resultado.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical')
  expect(graves.map((v) => `${page.url()} ${v.id}: ${v.nodes.map((n) => n.html.slice(0, 120)).slice(0, 3).join(' | ')}`)).toEqual([])
}

/** Não pode haver rolagem para o lado: a tela cabe na largura do aparelho. */
export async function semRolagemLateral(page: Page) {
  const larguras = await page.evaluate(() => [document.documentElement.scrollWidth, window.innerWidth])
  expect(larguras[0]).toBeLessThanOrEqual(larguras[1])
}

/** O código que um app autenticador mostraria agora (RFC 6238, HMAC-SHA1, 30 s). */
export function codigoTotp(chaveBase32: string, agora = Date.now()): string {
  const alfabeto = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
  let bits = ''
  for (const c of chaveBase32.replace(/=+$/, '').toUpperCase()) bits += alfabeto.indexOf(c).toString(2).padStart(5, '0')
  const chave = Buffer.from((bits.match(/.{8}/g) ?? []).map((b) => parseInt(b, 2)))
  const contador = Buffer.alloc(8)
  contador.writeBigInt64BE(BigInt(Math.floor(agora / 1000 / 30)))
  const hash = createHmac('sha1', chave).update(contador).digest()
  const d = hash[hash.length - 1] & 0x0f
  const numero = ((hash[d] & 0x7f) << 24) | (hash[d + 1] << 16) | (hash[d + 2] << 8) | hash[d + 3]
  return String(numero % 1_000_000).padStart(6, '0')
}

/** CNPJ válido novo a cada chamada: o cadastro recusa CNPJ repetido. */
export function cnpjNovo(): string {
  const d = Array.from({ length: 12 }, () => Math.floor(Math.random() * 10))
  for (const pesos of [[5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2], [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]]) {
    const resto = d.reduce((s, n, i) => s + n * pesos[i], 0) % 11
    d.push(resto < 2 ? 0 : 11 - resto)
  }
  return d.join('')
}
