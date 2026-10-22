import { expect, test } from '@playwright/test'
import { cnpjNovo, codigoTotp, semViolacaoGrave } from './apoio'

test('o dono liga o segundo fator e a entrada passa a pedir o código', async ({ page }) => {
  const email = `seguranca-${Date.now()}@revenda.dev`
  const senha = 'senha-de-teste-123'
  await page.goto('/cadastro')
  await page.locator('#empresa').fill('Revenda Segura')
  await page.locator('#cnpj').fill(cnpjNovo())
  await page.locator('#nome').fill('Dona Segura')
  await page.locator('#email').fill(email)
  await page.locator('#senha').fill(senha)
  await page.getByRole('button', { name: /Cadastrar/ }).click()
  await expect(page.getByRole('heading', { name: 'Início' })).toBeVisible()

  await page.goto('/conta/seguranca')
  await semViolacaoGrave(page)
  await page.getByRole('button', { name: 'Ligar com app autenticador' }).click()
  const chave = (await page.locator('code').innerText()).trim()
  await page.getByLabel('Código que o app mostra').fill(codigoTotp(chave))
  await page.getByRole('button', { name: 'Confirmar e ligar' }).click()
  await expect(page.getByRole('button', { name: 'Copiar códigos' })).toBeVisible()
  await expect(page.locator('ol li')).toHaveCount(10)

  await page.getByRole('button', { name: 'Sair' }).click()
  await page.getByLabel('E-mail').fill(email)
  await page.getByLabel('Senha').fill(senha)
  await page.getByRole('button', { name: 'Entrar' }).click()
  await expect(page.getByLabel('Código do app autenticador')).toBeVisible()
  await semViolacaoGrave(page)
  await page.getByLabel('Código do app autenticador').fill(codigoTotp(chave))
  await page.getByRole('button', { name: 'Entrar' }).click()
  await expect(page.getByRole('heading', { name: 'Início' })).toBeVisible()
})
