import { type Page, expect, test } from '@playwright/test'
import { CLIENTE_A, CLIENTE_B, SENHA, entrar } from './apoio'

/** Navega sem recarregar a página (como um link do app): o cache do TanStack Query continua vivo. */
async function irSemRecarregar(page: Page, caminho: string) {
  await page.evaluate((destino) => {
    window.history.pushState({}, '', destino)
    window.dispatchEvent(new PopStateEvent('popstate'))
  }, caminho)
  await expect(page).toHaveURL(new RegExp(`${caminho}$`))
}

async function sair(page: Page) {
  await page.getByRole('button', { name: /^Conta de / }).click()
  await page.getByRole('menuitem', { name: 'Sair' }).click()
  await expect(page).toHaveURL(/\/entrar$/)
}

test('quem entra depois, no mesmo navegador, não vê o que o anterior tinha carregado', async ({ page }) => {
  // A revenda A carrega as vendas dela.
  await entrar(page, CLIENTE_A)
  await irSemRecarregar(page, '/painel/vendas')
  await expect(page.getByText('Café da Esquina').first()).toBeVisible()
  await sair(page)

  // Respostas da B demoram: o que aparecer antes delas só pode ter vindo do cache.
  await page.route('**/api/vendas', async (rota) => {
    await new Promise((pronto) => setTimeout(pronto, 3_000))
    await rota.continue()
  })
  await page.getByLabel('E-mail').fill(CLIENTE_B)
  await page.getByLabel('Senha').fill(SENHA)
  await page.getByRole('button', { name: 'Entrar' }).click()
  await expect(page).not.toHaveURL(/\/entrar/)
  await irSemRecarregar(page, '/painel/vendas')
  await expect(page.getByRole('heading', { name: 'Vendas' })).toBeVisible()

  await page.waitForTimeout(800)
  await expect(page.getByText('Café da Esquina')).toHaveCount(0)
  await expect(page.getByText('Sorveteria Gelato').first()).toBeVisible({ timeout: 10_000 })
})
