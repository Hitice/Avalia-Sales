import { expect, test } from '@playwright/test'
import { CLIENTE_B, entrar, semViolacaoGrave } from './apoio'

// Pede e desiste no mesmo teste: a revenda B da demonstração volta como estava.
test('o dono pede o encerramento da conta, vê o distrato a caminho e desiste', async ({ page }) => {
  await entrar(page, CLIENTE_B)
  await page.goto('/conta/dados')
  await page.getByLabel('CEP').fill('38400192')
  await page.getByLabel('Rua ou avenida').fill('Avenida Rondon Pacheco')
  await page.getByLabel('Número').fill('1000')
  await page.getByLabel('Bairro').fill('Tabajaras')
  await page.getByLabel('Cidade').fill('Uberlândia')
  await page.getByLabel('UF').fill('MG')
  await page.getByRole('button', { name: 'Salvar' }).click()
  await expect(page.getByText('Dados salvos.').first()).toBeVisible()

  await page.goto('/painel/configuracoes')
  await page.getByRole('button', { name: 'Encerrar conta' }).click()
  const confirmacao = page.getByRole('dialog')
  await expect(confirmacao.getByText(/o estoque tem baixa sem devolução/)).toBeVisible()
  await confirmacao.getByRole('button', { name: 'Encerrar conta' }).click()
  // O axe mede com o modal já fechado: a camada escura saindo derruba o contraste.
  await expect(confirmacao).toHaveCount(0)

  await expect(page.getByText(/Preparando o distrato|Assine o distrato/)).toBeVisible()
  await semViolacaoGrave(page)

  await page.getByRole('button', { name: 'Desistir' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Desistir' }).click()
  await expect(page.getByRole('button', { name: 'Encerrar conta' })).toBeVisible()
})
