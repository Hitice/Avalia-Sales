import { expect, test } from '@playwright/test'
import { entrar, semViolacaoGrave } from './apoio'

test('o vendedor convidado cria a senha pelo link e cai no painel dele', async ({ page, browser }) => {
  await entrar(page)
  await page.goto('/painel/equipe')
  const email = `convite-${Date.now()}@revenda-a.dev`
  await page.getByLabel('Nome').fill('Vendedor de Teste')
  await page.getByLabel('E-mail de acesso').fill(email)
  await page.getByRole('button', { name: 'Cadastrar vendedor' }).click()
  const link = await page.getByLabel('Link do convite').inputValue()
  expect(link).toContain('/convite#')

  const outro = await browser.newContext({ viewport: { width: 390, height: 844 } })
  const vendedor = await outro.newPage()
  await vendedor.goto(new URL(link).pathname + new URL(link).hash)
  await semViolacaoGrave(vendedor)
  await vendedor.getByLabel('Senha', { exact: true }).fill('senha-do-vendedor-1')
  await vendedor.getByLabel('Repita a senha').fill('senha-do-vendedor-1')
  await vendedor.getByRole('button', { name: 'Gravar senha' }).click()
  await expect(vendedor).toHaveURL(/\/vendedor\/inicio/)
  await expect(vendedor.getByText('Comissão a receber')).toBeVisible()
  await semViolacaoGrave(vendedor)
  await outro.close()
})
