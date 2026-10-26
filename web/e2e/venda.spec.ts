import { expect, test } from '@playwright/test'
import { entrar, placaLivre, semRolagemLateral, semViolacaoGrave } from './apoio'

test('venda no balcão: a placa como item (QR e destino), cliente e cobrança, com o link curto no fim', async ({ page }) => {
  const codigo = await placaLivre()
  await entrar(page)

  await page.goto('/vender')
  await semViolacaoGrave(page)
  // A placa é um item do catálogo: tocar nela abre a leitura do QR e o destino.
  await page.getByRole('list', { name: 'Itens do catálogo' }).getByRole('button').first().click()
  await page.getByLabel('Ou o link de avaliação do perfil').fill('instagram.com/padaria')
  await expect(page.getByText('Não é um link do Google')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Adicionar à venda' })).toBeDisabled()
  await page.getByLabel('Ou o link de avaliação do perfil').fill('https://search.google.com/local/writereview?placeid=ChIJE2eTesteDePonta')

  await page.getByLabel('Código da placa').fill(codigo)
  await page.getByRole('button', { name: 'Conferir' }).click()
  await expect(page.getByText('no estoque')).toBeVisible()
  await page.getByRole('button', { name: 'Adicionar à venda' }).click()
  await expect(page.getByRole('list', { name: 'Itens da venda' })).toContainText(codigo)
  await semViolacaoGrave(page)
  await page.getByRole('button', { name: /^Continuar/ }).click()

  await page.getByLabel('Empresa').fill('Padaria do Teste')
  await page.getByLabel('Responsável').fill('Marta')
  await page.getByLabel('Telefone com DDD').fill('34999990000')
  await page.getByLabel('CNPJ ou CPF').fill('529.982.247-25')
  await page.getByLabel('E-mail').fill('marta@padaria.com.br')
  await semViolacaoGrave(page)
  await page.getByRole('button', { name: 'Ir para a cobrança' }).click()

  await expect(page.getByRole('list', { name: 'Resumo da venda' })).toContainText(codigo)
  await page.getByRole('button', { name: 'Registrar venda' }).click()
  await expect(page.getByRole('heading', { name: 'Venda registrada' })).toBeVisible()
  await expect(page.getByLabel('Link de avaliação')).toHaveValue(/\/l\/[0-9a-z]{6}$/)
  await semRolagemLateral(page)
})

test('serviço entra com o pedido mínimo e não desce dele', async ({ page }) => {
  await entrar(page)
  await page.goto('/vender')
  const comMinimo = page.getByRole('list', { name: 'Itens do catálogo' }).getByRole('button', { name: /mínimo (\d+)/ })
  test.skip((await comMinimo.count()) === 0, 'O catálogo da demonstração não tem serviço com pedido mínimo.')
  const minimo = Number(/mínimo (\d+)/.exec((await comMinimo.first().innerText()) ?? '')?.[1])
  await comMinimo.first().click()

  const quantidade = page.getByRole('list', { name: 'Itens da venda' }).getByLabel(/^Quantidade de/)
  await expect(quantidade).toHaveValue(String(minimo))
  await page.getByRole('button', { name: /^Menos / }).click()
  await expect(quantidade).toHaveValue(String(minimo))
  await expect(page.getByText(`Mínimo ${minimo}`).first()).toBeVisible()
  // Com serviço na venda, o teste gratuito não aparece na cobrança.
  await page.getByRole('button', { name: /^Continuar/ }).click()
  await page.getByLabel('Empresa').fill('Padaria do Teste')
  await page.getByLabel('Responsável').fill('Marta')
  await page.getByLabel('Telefone com DDD').fill('34999990000')
  await page.getByRole('button', { name: 'Ir para a cobrança' }).click()
  await expect(page.getByRole('radio', { name: 'Teste 7 dias' })).toHaveCount(0)
  await semViolacaoGrave(page)
})

test('voltar no primeiro passo sai da venda', async ({ page }) => {
  await entrar(page)
  await page.getByRole('link', { name: 'Vender' }).click()
  await page.getByRole('button', { name: 'Voltar' }).click()
  await expect(page).not.toHaveURL(/\/vender/)
})

test('o Catálogo tem tela própria no menu, e a aba antiga de Vendas leva para lá', async ({ page }) => {
  await entrar(page)
  await page.goto('/painel/vendas?aba=catalogo')
  await expect(page).toHaveURL(/\/catalogo$/)
  await expect(page.getByRole('heading', { level: 1, name: 'Catálogo' })).toBeVisible()
  await expect(page.getByRole('link', { name: 'Pedir placas' })).toBeVisible()
  await semViolacaoGrave(page)
})

test('a busca do negócio no Google, na aba Maps, chama a API sem recarregar a página', async ({ page }) => {
  await entrar(page)
  await page.goto('/links?aba=maps')
  const busca = page.waitForResponse((r) => r.url().includes('/api/lugares'))
  await page.getByLabel('Nome do negócio no Google').fill('subway uberlandia')
  await page.getByLabel('Nome do negócio no Google').press('Enter')
  await busca
  // Sem chave do Google no ambiente de teste, a API responde que a busca não está ligada.
  await expect(page.getByText('A busca no Google não está ligada')).toBeVisible()
  await expect(page).toHaveURL(/\/links\?aba=maps$/)
})
