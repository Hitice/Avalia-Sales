import { expect, test } from '@playwright/test'
import { CLIENTE_B, SENHA, cnpjNovo, codigoTotp, semViolacaoGrave } from './apoio'

test('revenda recém-cadastrada só vê a confirmação do e-mail', async ({ page }) => {
  await page.goto('/cadastro')
  await page.locator('#empresa').fill('Revenda Nova')
  await page.locator('#cnpj').fill(cnpjNovo())
  await page.locator('#celular').fill('34999990000')
  await page.locator('#nome').fill('Dona Nova')
  await page.locator('#email').fill(`nova-${Date.now()}@revenda.dev`)
  await page.locator('#senha').fill('senha-de-teste-123')
  await page.getByRole('button', { name: /Cadastrar/ }).click()

  await expect(page.getByRole('heading', { name: 'Confirme seu e-mail' })).toBeVisible()
  await semViolacaoGrave(page)
  await expect(page.getByRole('navigation', { name: 'Menu principal' })).toHaveCount(0)
  await page.goto('/painel/vendas')
  await expect(page.getByRole('heading', { name: 'Confirme seu e-mail' })).toBeVisible()
})

// Conta da demonstração com e-mail confirmado; o teste desliga o fator no fim para não sujar o banco.
test('o dono liga o segundo fator, a entrada pede o código e ele desliga', async ({ page }) => {
  const email = CLIENTE_B
  const senha = SENHA
  const entrar = async () => {
    if (!page.url().endsWith('/entrar')) await page.goto('/entrar')
    await page.getByLabel('E-mail').fill(email)
    await page.getByLabel('Senha').fill(senha)
    await page.getByRole('button', { name: 'Entrar' }).click()
  }
  await entrar()
  await expect(page.getByRole('heading', { name: 'Início' })).toBeVisible()

  await page.goto('/conta/seguranca')
  await semViolacaoGrave(page)
  await page.getByRole('button', { name: 'Ligar com app autenticador' }).click()
  const chave = (await page.locator('code').innerText()).trim()
  await page.getByLabel('Código que o app mostra').fill(codigoTotp(chave))
  await page.getByRole('button', { name: 'Confirmar e ligar' }).click()
  await expect(page.locator('ol li')).toHaveCount(10)

  await page.getByRole('button', { name: 'Sair' }).click()
  // Espera a saída terminar: antes disso o cookie ainda vale e a entrada devolveria ao painel.
  await expect(page).toHaveURL(/\/entrar$/)
  await entrar()
  await expect(page.getByLabel('Código do app autenticador')).toBeVisible()
  await semViolacaoGrave(page)
  await page.getByLabel('Código do app autenticador').fill(codigoTotp(chave))
  await page.getByRole('button', { name: 'Entrar' }).click()
  await expect(page.getByRole('heading', { name: 'Início' })).toBeVisible()

  await page.goto('/conta/seguranca')
  await page.getByLabel('Código do app ou de recuperação').fill(codigoTotp(chave))
  await page.getByRole('button', { name: 'Desligar' }).click()
  await expect(page.getByText('Desligado')).toBeVisible()
})
