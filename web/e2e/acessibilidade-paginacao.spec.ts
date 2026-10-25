import { expect, test } from '@playwright/test'
import { CLIENTE_A, apiComo, entrar, semRolagemLateral, semViolacaoGrave } from './apoio'

/**
 * Lista paginada pela API de ponta a ponta, no celular e no computador: a primeira página tem no máximo 50
 * linhas, a segunda traz o resto sem repetir, a busca vai à API (acha o que está fora da página aberta) e a
 * tela não tem violação grave do axe nem rolagem para o lado.
 */
test('clientes: segunda página, busca na API, sem violação grave e sem rolagem para o lado', async ({ page }) => {
  const marca = `Pag${Date.now().toString(36)}`
  const dono = await apiComo(CLIENTE_A)
  for (let i = 0; i < 55; i++) {
    const telefone = `119${String(Date.now() % 10_000).padStart(4, '0')}${String(i).padStart(4, '0')}`
    const resposta = await dono.post('clientes', { data: { nome: `${marca} Cliente ${String(i).padStart(2, '0')}`, responsavel: 'Rita', telefone, documento: null, email: null, lugarNoGoogle: null } })
    expect(resposta.ok(), `cliente ${i}`).toBeTruthy()
  }

  await entrar(page)
  await page.goto('/painel/clientes')
  await page.getByRole('heading', { level: 1, name: 'Clientes' }).waitFor()
  await page.getByRole('searchbox', { name: 'Buscar cliente' }).fill(marca)

  const rodape = page.getByRole('navigation', { name: 'Paginação' })
  await expect(rodape.getByText('1 a 50 de 55')).toBeVisible()
  await expect(page.locator('tbody tr')).toHaveCount(50)
  await semViolacaoGrave(page)
  await semRolagemLateral(page)

  await rodape.getByRole('button', { name: 'Próxima' }).click()
  await expect(rodape.getByText('51 a 55 de 55')).toBeVisible()
  await expect(page.locator('tbody tr')).toHaveCount(5)
  await expect(rodape.getByRole('button', { name: 'Próxima' })).toBeDisabled()
  await semViolacaoGrave(page)
  await semRolagemLateral(page)

  // Um cliente que não está na página aberta: só a busca da API o encontra.
  await page.getByRole('searchbox', { name: 'Buscar cliente' }).fill(`${marca} Cliente 03`)
  await expect(page.locator('tbody tr')).toHaveCount(1)
  await expect(page.getByText(`${marca} Cliente 03`).first()).toBeVisible()
  await expect(rodape).toHaveCount(0)

  await page.getByRole('searchbox', { name: 'Buscar cliente' }).fill(`${marca} nada com isso`)
  await expect(page.getByRole('status').filter({ hasText: 'Nada na lista com' })).toBeVisible()
  await semViolacaoGrave(page)
  await dono.dispose()
})
