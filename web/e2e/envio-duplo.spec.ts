import { type Page, expect, test } from '@playwright/test'
import { entrar } from './apoio'

/** Do catálogo à cobrança com um serviço de pedido mínimo 1. */
async function ateACobranca(page: Page) {
  await entrar(page)
  await page.goto('/vender')
  await page.getByRole('list', { name: 'Itens do catálogo' }).getByRole('button', { name: /QR Code Dinâmico Pro/ }).click()
  await page.getByRole('button', { name: /^Continuar/ }).click()
  await page.getByLabel('Empresa').fill('Envio Duplo Ltda')
  await page.getByLabel('Responsável').fill('Marta')
  await page.getByLabel('Telefone com DDD').fill('34999990000')
  await page.getByRole('button', { name: 'Ir para a cobrança' }).click()
}

/** Conta os POST de venda e segura cada um por 1 s, como numa rede lenta. */
async function contarEnvios(page: Page) {
  const contagem = { envios: 0 }
  await page.route('**/api/vendas/itens', async (rota) => {
    contagem.envios++
    await new Promise((pronto) => setTimeout(pronto, 1_000))
    await rota.continue()
  })
  return contagem
}

test('toque duplo em Registrar venda (Pix) envia a venda uma vez só', async ({ page }) => {
  const contagem = await contarEnvios(page)
  await ateACobranca(page)

  await page.getByRole('button', { name: 'Registrar venda' }).dblclick()

  await expect(page.getByRole('heading', { name: 'Venda registrada' })).toBeVisible()
  expect(contagem.envios).toBe(1)
})

test('toque duplo em Cobrar no cartão envia a venda uma vez só: cada toque geraria um token novo', async ({ page }) => {
  const contagem = await contarEnvios(page)
  await ateACobranca(page)
  await page.getByRole('radio', { name: 'Cartão' }).click()
  await page.getByLabel('Número do cartão').fill('4242424242424242')
  await page.getByLabel('Nome impresso').fill('MARTA SILVA')
  await page.getByLabel('Validade').fill('12/39')
  await page.getByLabel('Código').fill('123')

  await page.getByRole('button', { name: 'Cobrar no cartão' }).dblclick()

  await expect(page.getByRole('heading', { name: 'Venda registrada' })).toBeVisible()
  expect(contagem.envios).toBe(1)
})

test('queda de rede depois de a API registrar: o segundo toque leva a mesma chave e recebe a mesma venda', async ({ page }) => {
  const chaves: (string | undefined)[] = []
  const repetidas: (string | undefined)[] = []
  await page.route('**/api/vendas/itens', async (rota) => {
    chaves.push(rota.request().headers()['idempotency-key'])
    const resposta = await rota.fetch()
    repetidas.push(resposta.headers()['idempotent-replayed'])
    // A primeira chega à API e é registrada, mas a resposta se perde no caminho.
    if (chaves.length === 1) await rota.abort('connectionreset')
    else await rota.fulfill({ response: resposta })
  })
  await ateACobranca(page)

  await page.getByRole('button', { name: 'Registrar venda' }).click()
  await expect(page.getByText('Sem conexão com o servidor.')).toBeVisible()
  await page.getByRole('button', { name: 'Registrar venda' }).click()

  await expect(page.getByRole('heading', { name: 'Venda registrada' })).toBeVisible()
  expect(chaves).toHaveLength(2)
  expect(chaves[1]).toBe(chaves[0])
  expect(repetidas).toEqual([undefined, 'true'])
})
