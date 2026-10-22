import { expect, test } from '@playwright/test'
import { entrar, placaLivre, semRolagemLateral, semViolacaoGrave } from './apoio'

test('venda no balcão: negócio, placa, cliente e cobrança, com o link curto no fim', async ({ page }) => {
  const codigo = await placaLivre()
  await entrar(page)

  await page.goto('/vender')
  await semViolacaoGrave(page)
  await page.getByLabel('Ou o link de avaliação do perfil').fill('instagram.com/padaria')
  await expect(page.getByText('Não é um link do Google')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Ler a placa' })).toBeDisabled()
  await page.getByLabel('Ou o link de avaliação do perfil').fill('https://search.google.com/local/writereview?placeid=ChIJE2eTesteDePonta')
  await page.getByRole('button', { name: 'Ler a placa' }).click()

  await page.getByLabel('Código da placa').fill(codigo)
  await page.getByRole('button', { name: 'Conferir' }).click()
  await expect(page.getByText('no estoque')).toBeVisible()
  await page.getByRole('button', { name: 'Continuar' }).click()

  await page.getByLabel('Empresa').fill('Padaria do Teste')
  await page.getByLabel('Responsável').fill('Marta')
  await page.getByLabel('Telefone com DDD').fill('34999990000')
  await page.getByLabel('CNPJ ou CPF').fill('529.982.247-25')
  await page.getByLabel('E-mail').fill('marta@padaria.com.br')
  await semViolacaoGrave(page)
  await page.getByRole('button', { name: 'Ir para a cobrança' }).click()

  await page.getByRole('combobox').click()
  await page.getByRole('option').first().click()
  await page.getByRole('button', { name: 'Registrar venda' }).click()
  await expect(page.getByRole('heading', { name: 'Venda registrada' })).toBeVisible()
  await expect(page.getByLabel('Link de avaliação')).toHaveValue(/\/l\/[0-9a-z]{6}$/)
  await semRolagemLateral(page)
})

test('voltar no primeiro passo sai da venda', async ({ page }) => {
  await entrar(page)
  await page.getByRole('link', { name: 'Vender' }).click()
  await page.getByRole('button', { name: 'Voltar' }).click()
  await expect(page).not.toHaveURL(/\/vender/)
})
