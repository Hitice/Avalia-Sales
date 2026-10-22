import { expect, test } from '@playwright/test'
import { entrar, placaLivre } from './apoio'

/** No simulador, cartão aprova na hora; final 0002 simula recusa. */
for (const [numero, esperado] of [['4000 0000 0000 0010', 'Pagamento confirmado'], ['4000 0000 0000 0002', 'Pagamento falhou']] as const) {
  test(`venda no cartão ${numero.slice(-4)}: ${esperado.toLowerCase()}`, async ({ page }) => {
    await entrar(page)
    const codigo = await placaLivre()

    await page.goto('/vender')
    await page.getByLabel('Ou o link de avaliação do perfil').fill('g.page/r/CartaoDeTeste/review')
    await page.getByRole('button', { name: 'Configurar' }).click()
    await page.getByLabel('Código da placa').fill(codigo)
    await page.getByRole('button', { name: 'Conferir' }).click()
    await page.getByRole('button', { name: 'Continuar' }).click()
    await page.getByLabel('Empresa').fill('Loja do Cartão')
    await page.getByLabel('Responsável').fill('Rui')
    await page.getByLabel('Telefone com DDD').fill('34988887777')
    await page.getByLabel('CNPJ ou CPF').fill('529.982.247-25')
    await page.getByLabel('E-mail').fill('rui@loja.com.br')
    await page.getByRole('button', { name: 'Ir para a cobrança' }).click()

    await page.getByRole('combobox').first().click()
    await page.getByRole('option').first().click()
    await page.getByRole('tab', { name: 'Cartão' }).click()
    await expect(page.getByRole('button', { name: 'Cobrar no cartão' })).toBeDisabled()
    await page.getByLabel('Número do cartão').fill(numero)
    await page.getByLabel('Nome impresso').fill('RUI TESTE')
    await page.getByLabel('Validade').fill('12/30')
    await page.getByLabel('Código').fill('123')
    await page.getByRole('button', { name: 'Cobrar no cartão' }).click()

    await expect(page.getByRole('heading', { name: 'Venda registrada' })).toBeVisible()
    await expect(page.getByText(esperado)).toBeVisible()
  })
}
