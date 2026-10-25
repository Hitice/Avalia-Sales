import { type Browser, expect, test } from '@playwright/test'
import { CLIENTE_A, CLIENTE_B, OPERADOR, VENDEDOR_A, VENDEDOR_B, apiComo, cpfNovo, entrar } from './apoio'

/** Um navegador por papel: computador para a operadora e a revenda, celular para a vendedora. */
async function abrir(browser: Browser, email: string, celular = false) {
  const contexto = await browser.newContext({ viewport: celular ? { width: 390, height: 844 } : { width: 1280, height: 900 }, reducedMotion: 'reduce' })
  const page = await contexto.newPage()
  await entrar(page, email)
  return page
}

test('jornada entre operadora, revenda e vendedora, e cada um vê só o que é dele', async ({ browser }) => {
  test.setTimeout(180_000)
  const negocio = `Padaria Jornada ${Date.now()}`

  // 1. A operadora gera um lote de 10.
  const operador = await abrir(browser, OPERADOR)
  await operador.goto('/operadora/lotes')
  await expect(operador.locator('tbody tr').first()).toBeVisible()
  const antes = await operador.locator('tbody tr td:first-child').allInnerTexts()
  await operador.getByLabel('Placas').fill('10')
  await operador.getByRole('button', { name: 'Gerar lote' }).click()
  await expect(operador.locator('tbody tr')).toHaveCount(antes.length + 1)
  const lote = (await operador.locator('tbody tr td:first-child').allInnerTexts()).find((r) => !antes.includes(r))!.trim()
  expect(lote).toMatch(/^LT-\d{4}$/)

  // 2. A revenda A pede o pacote de 10.
  const cliente = await abrir(browser, CLIENTE_A)
  await cliente.goto('/painel/pedidos')
  await expect(cliente.getByText('10 placas', { exact: true })).toBeVisible()
  await cliente.getByRole('button', { name: 'Pedir' }).first().click()

  // 3. A operadora registra o pagamento e despacha esse lote para a revenda A.
  const doPedido = () => operador.getByRole('row').filter({ hasText: 'Revenda Demonstração A' }).filter({ has: operador.getByRole('cell', { name: '10', exact: true }) })
  // O pedido da revenda pode ainda estar sendo gravado: recarrega até ele aparecer.
  await expect(async () => {
    await operador.goto('/operadora/pedidos')
    await expect(doPedido().filter({ hasText: 'Aguardando' }).first()).toBeVisible({ timeout: 2_000 })
  }).toPass({ timeout: 20_000 })
  // No computador, as ações da linha ficam no "⋯".
  await doPedido().filter({ hasText: 'Aguardando' }).first().getByRole('button', { name: /^Ações de/ }).click()
  await operador.getByRole('menuitem', { name: 'Registrar pagamento' }).click()
  await doPedido().filter({ hasText: 'Pago' }).first().getByRole('button', { name: /^Ações de/ }).click()
  await operador.getByRole('menuitem', { name: 'Despachar' }).click()
  const despacho = operador.getByRole('dialog')
  await despacho.getByLabel('Lote').click()
  await operador.getByRole('option', { name: lote }).click()
  await despacho.getByRole('button', { name: 'Despachar' }).click()
  await expect(doPedido().filter({ hasText: lote })).toBeVisible()

  // 4. A revenda ativa o lote e consigna uma placa à vendedora.
  await cliente.goto('/painel/pedidos')
  await cliente.getByLabel('Lote', { exact: true }).fill(lote)
  await cliente.getByRole('button', { name: 'Ativar lote' }).click()
  await expect(cliente.getByRole('row').filter({ hasText: lote }).filter({ hasText: 'Entregue' })).toBeVisible()
  await cliente.goto('/painel/estoque')
  await cliente.getByLabel('Vendedor').click()
  await cliente.getByRole('option', { name: 'Bruna Lima' }).click()
  await cliente.getByLabel('Quantidade').fill('1')
  await cliente.getByRole('button', { name: 'Consignar' }).click()

  // 5. A vendedora vê a placa com ela e vende no celular, no Pix.
  const vendedora = await abrir(browser, VENDEDOR_A, true)
  await vendedora.goto('/vendedor/placas')
  const codigo = (await vendedora.getByRole('row').filter({ hasText: 'Em estoque' }).first().locator('td').first().innerText()).trim()
  await vendedora.goto('/vender')
  // A placa é um item do catálogo: abre a leitura do QR e o destino numa folha.
  await vendedora.getByRole('list', { name: 'Itens do catálogo' }).getByRole('button').first().click()
  await vendedora.getByRole('tab', { name: 'Link do cliente' }).click()
  await vendedora.getByLabel('Endereço').fill('cardapio.jornada.com.br')
  await vendedora.getByLabel('Código da placa').fill(codigo)
  await vendedora.getByRole('button', { name: 'Conferir' }).click()
  await vendedora.getByRole('button', { name: 'Adicionar à venda' }).click()
  await vendedora.getByRole('button', { name: /^Continuar/ }).click()
  await vendedora.getByLabel('Empresa').fill(negocio)
  await vendedora.getByLabel('Responsável').fill('Marta')
  // Documento e telefone novos: a venda reaproveita o cliente por qualquer um dos dois.
  await vendedora.getByLabel('Telefone com DDD').fill(`349${String(Date.now()).slice(-8)}`)
  await vendedora.getByLabel('CNPJ ou CPF').fill(cpfNovo())
  await vendedora.getByLabel('E-mail').fill('marta@jornada.com.br')
  await vendedora.getByRole('button', { name: 'Ir para a cobrança' }).click()
  await vendedora.getByRole('button', { name: 'Registrar venda' }).click()
  await expect(vendedora.getByRole('heading', { name: 'Venda registrada' })).toBeVisible()
  // O Pix simulado paga em 20 s e o Worker confere a cobrança pendente: a tela confirma sozinha.
  await expect(vendedora.getByText('Pagamento confirmado')).toBeVisible({ timeout: 90_000 })

  // 6. A revenda vê a venda dela, com a vendedora, e a comissão a pagar.
  await cliente.goto('/painel/vendas')
  await expect(cliente.getByRole('row').filter({ hasText: negocio }).filter({ hasText: 'Bruna Lima' })).toBeVisible()
  await cliente.goto('/painel/comissoes')
  await expect(cliente.getByRole('row').filter({ hasText: 'Bruna Lima' }).first()).toBeVisible()
  await vendedora.goto('/vendedor/vendas')
  await expect(vendedora.getByText(negocio)).toBeVisible()

  // 7. Isolamento: a revenda B e o vendedor dela não veem nada da A, nem pela API.
  const clienteB = await abrir(browser, CLIENTE_B)
  await clienteB.goto('/painel/vendas')
  await expect(clienteB.getByRole('heading', { name: 'Vendas' })).toBeVisible()
  await expect(clienteB.getByText(negocio)).toHaveCount(0)
  const vendedorB = await abrir(browser, VENDEDOR_B, true)
  await vendedorB.goto('/vendedor/vendas')
  await expect(vendedorB.getByRole('heading', { level: 1 })).toBeVisible()
  await expect(vendedorB.getByText(negocio)).toHaveCount(0)
  const apiB = await apiComo(CLIENTE_B)
  expect((await apiB.get(`placas/${codigo}`)).ok()).toBeFalsy()
  const vendasB = await (await apiB.get('vendas')).json() as { codigo: string }[]
  expect(vendasB.some((v) => v.codigo === codigo)).toBeFalsy()
  await apiB.dispose()

  // 8. A operadora vê o pedido da revenda A ativado.
  await operador.goto('/operadora/pedidos')
  await expect(doPedido().filter({ hasText: lote }).filter({ hasText: 'Entregue' })).toBeVisible()
})
