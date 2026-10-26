import { expect, test } from '@playwright/test'
import { OPERADOR, VENDEDOR_A, entrar, semRolagemLateral, semViolacaoGrave } from './apoio'

const TELAS_DO_DONO = ['/painel/inicio', '/painel/vendas', '/carteira', '/painel/estoque', '/painel/pedidos', '/painel/equipe', '/painel/clientes', '/links',
  '/painel/comissoes', '/painel/financeiro', '/painel/razao', '/painel/mapa', '/catalogo', '/painel/configuracoes', '/painel/marca', '/painel/auditoria', '/painel/suporte']

const TELAS_DA_OPERADORA = ['/operadora/pedidos', '/operadora/lotes', '/operadora/servicos', '/operadora/catalogo', '/operadora/pacotes',
  '/operadora/cupons', '/operadora/revendedores', '/operadora/atendimento', '/operadora/erp', '/operadora/auditoria', '/operadora/financeiro']

for (const [quem, email, telas] of [['dono', undefined, TELAS_DO_DONO], ['operadora', OPERADOR, TELAS_DA_OPERADORA]] as const) {
test(`telas da ${quem === 'dono' ? 'revenda' : 'operadora'} sem violação grave e sem rolagem para o lado`, async ({ page }) => {
  await entrar(page, email)
  for (const tela of telas) {
    await test.step(tela, async () => {
      await page.goto(tela)
      await page.getByRole('heading', { level: 1 }).waitFor()
      await page.waitForLoadState('networkidle')
      await semViolacaoGrave(page)
      await semRolagemLateral(page)
    })
  }
})
}

test('detalhe da venda abre pela lista, sem violação grave e sem rolagem para o lado', async ({ page }) => {
  await entrar(page)
  await page.goto('/painel/vendas')
  await page.locator('a[href^="/painel/vendas/"]').first().click()
  await page.getByText('Pagamento', { exact: true }).waitFor()
  await page.waitForLoadState('networkidle')
  await semViolacaoGrave(page)
  await semRolagemLateral(page)
})

test('telas do vendedor sem violação grave e sem rolagem para o lado', async ({ page }) => {
  await entrar(page, VENDEDOR_A)
  for (const tela of ['/vendedor/inicio', '/vendedor/placas', '/vendedor/vendas', '/vender', '/carteira', '/catalogo', '/links']) {
    await test.step(tela, async () => {
      await page.goto(tela)
      await page.getByRole('heading', { level: 1 }).waitFor()
      await page.waitForLoadState('networkidle')
      await semViolacaoGrave(page)
      await semRolagemLateral(page)
    })
  }
})

test('ficha da placa abre pela lista de vendas, sem violação grave e sem rolagem para o lado', async ({ page }) => {
  await entrar(page)
  await page.goto('/painel/vendas')
  await page.locator('a[href^="/placa/"]').first().click()
  await page.getByRole('heading', { level: 1 }).waitFor()
  await page.waitForLoadState('networkidle')
  await semViolacaoGrave(page)
  await semRolagemLateral(page)
})

test('diálogo prende o foco, fecha no Esc e devolve o foco ao botão que abriu', async ({ page }) => {
  await entrar(page, VENDEDOR_A)
  await page.goto('/vender')
  await page.getByRole('list', { name: 'Itens do catálogo' }).getByRole('button').first().click()
  const dialogo = page.getByRole('dialog')
  await dialogo.waitFor()
  // Tab repetido não sai do diálogo.
  for (let i = 0; i < 12; i++) await page.keyboard.press('Tab')
  expect(await page.evaluate(() => !!document.activeElement?.closest('[role="dialog"]'))).toBe(true)

  await page.keyboard.press('Escape')
  await expect(dialogo).toHaveCount(0)
  // O Radix devolve o foco num setTimeout depois de desmontar.
  await expect.poll(() => page.evaluate(() => document.activeElement?.closest('[aria-label="Itens do catálogo"]') !== null)).toBe(true)
})
