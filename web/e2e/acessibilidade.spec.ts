import { test } from '@playwright/test'
import { OPERADOR, entrar, semRolagemLateral, semViolacaoGrave } from './apoio'

const TELAS_DO_DONO = ['/painel/inicio', '/painel/vendas', '/painel/estoque', '/painel/pedidos', '/painel/equipe', '/painel/clientes', '/links',
  '/painel/comissoes', '/painel/financeiro', '/painel/razao', '/painel/mapa', '/catalogo', '/painel/configuracoes', '/painel/marca', '/painel/auditoria', '/painel/suporte']

const TELAS_DA_OPERADORA = ['/operadora/pedidos', '/operadora/lotes', '/operadora/servicos', '/operadora/catalogo', '/operadora/pacotes',
  '/operadora/cupons', '/operadora/revendedores', '/operadora/atendimento', '/operadora/erp', '/operadora/auditoria']

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
