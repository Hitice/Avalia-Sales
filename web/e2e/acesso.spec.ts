import { expect, test } from '@playwright/test'
import { CLIENTE_A, entrar, semViolacaoGrave } from './apoio'

test('senha errada avisa sem dizer se o e-mail existe', async ({ page }) => {
  await page.goto('/entrar')
  await semViolacaoGrave(page)
  await page.getByLabel('E-mail').fill(CLIENTE_A)
  await page.getByLabel('Senha').fill('senha-errada-123')
  await page.getByRole('button', { name: 'Entrar' }).click()
  await expect(page.getByText('E-mail ou senha não conferem.')).toBeVisible()
})

test('o dono entra no painel e recarregar a página mantém o login', async ({ page }) => {
  await entrar(page)
  await expect(page).toHaveURL(/\/painel\/inicio/)
  await page.reload()
  await expect(page.getByRole('heading', { name: 'Início' })).toBeVisible()
})
