import AxeBuilder from '@axe-core/playwright'
import { type Page, expect } from '@playwright/test'

/** Logins da demonstração (Demonstracao.cs). */
export const DONO = 'dono@revenda-a.dev'
export const SENHA = 'demonstracao-123'

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
