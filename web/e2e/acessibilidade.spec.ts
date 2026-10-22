import { test } from '@playwright/test'
import { entrar, semRolagemLateral, semViolacaoGrave } from './apoio'

const TELAS_DO_DONO = ['/painel/inicio', '/painel/vendas', '/painel/estoque', '/painel/pedidos', '/painel/equipe', '/painel/clientes', '/links',
  '/painel/comissoes', '/painel/razao', '/painel/mapa', '/painel/catalogo', '/painel/marca', '/painel/suporte']

test('telas do dono sem violação grave e sem rolagem para o lado', async ({ page }) => {
  await entrar(page)
  for (const tela of TELAS_DO_DONO) {
    await test.step(tela, async () => {
      await page.goto(tela)
      await page.getByRole('heading', { level: 1 }).waitFor()
      await page.waitForLoadState('networkidle')
      await semViolacaoGrave(page)
      await semRolagemLateral(page)
    })
  }
})
