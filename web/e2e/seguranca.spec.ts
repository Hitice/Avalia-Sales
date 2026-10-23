import { expect, test } from '@playwright/test'
import { CLIENTE_A, CLIENTE_B, SENHA, apiComo, cnpjNovo, codigoTotp, entrar, semViolacaoGrave } from './apoio'

test('revenda recém-cadastrada só vê a confirmação do e-mail', async ({ page }) => {
  await page.goto('/cadastro')
  await page.locator('#empresa').fill('Revenda Nova')
  await page.locator('#cnpj').fill(cnpjNovo())
  await page.locator('#celular').fill('34999990000')
  await page.getByLabel('CEP').fill('38400192')
  await page.getByLabel('Rua ou avenida').fill('Avenida Princesa Isabel')
  await page.getByLabel('Número').fill('100')
  await page.getByLabel('Bairro').fill('Tabajaras')
  await page.getByLabel('Cidade').fill('Uberlândia')
  await page.getByLabel('UF').fill('MG')
  await page.locator('#nome').fill('Dona Nova')
  await page.locator('#email').fill(`nova-${Date.now()}@revenda.dev`)
  await page.locator('#senha').fill('senha-de-teste-123')
  await page.getByRole('button', { name: /Cadastrar/ }).click()

  await expect(page.getByRole('heading', { name: 'Confirme seu e-mail' })).toBeVisible()
  await semViolacaoGrave(page)
  await expect(page.getByRole('navigation', { name: 'Menu principal' })).toHaveCount(0)
  await page.goto('/painel/vendas')
  await expect(page.getByRole('heading', { name: 'Confirme seu e-mail' })).toBeVisible()
})

// Conta da demonstração com e-mail confirmado; o teste desliga o fator no fim para não sujar o banco.
test('o dono liga o segundo fator, a entrada pede o código e ele desliga', async ({ page }) => {
  const email = CLIENTE_B
  const senha = SENHA
  const entrar = async () => {
    if (!page.url().endsWith('/entrar')) await page.goto('/entrar')
    await page.getByLabel('E-mail').fill(email)
    await page.getByLabel('Senha').fill(senha)
    await page.getByRole('button', { name: 'Entrar' }).click()
  }
  await entrar()
  await expect(page.getByRole('heading', { name: 'Início' })).toBeVisible()

  await page.goto('/conta/seguranca')
  await semViolacaoGrave(page)
  await page.getByRole('button', { name: 'Ativar com app autenticador' }).click()
  const chave = (await page.locator('code').innerText()).trim()
  await page.getByLabel('Código que o app mostra').fill(codigoTotp(chave))
  await page.getByRole('button', { name: 'Confirmar e ativar' }).click()
  await expect(page.locator('ol li')).toHaveCount(10)

  await page.getByRole('button', { name: /^Conta de / }).click()
  await page.getByRole('menuitem', { name: 'Sair' }).click()
  // Espera a saída terminar: antes disso o cookie ainda vale e a entrada devolveria ao painel.
  await expect(page).toHaveURL(/\/entrar$/)
  await entrar()
  await expect(page.getByLabel('Código do app autenticador')).toBeVisible()
  await semViolacaoGrave(page)
  await page.getByLabel('Código do app autenticador').fill(codigoTotp(chave))
  await page.getByRole('button', { name: 'Entrar' }).click()
  await expect(page.getByRole('heading', { name: 'Início' })).toBeVisible()

  await page.goto('/conta/seguranca')
  await page.getByRole('button', { name: 'Desativar' }).click()
  // O código vai para o e-mail; o do app também vale, e é o que o teste tem.
  await page.getByLabel('Código enviado para o seu e-mail').fill(codigoTotp(chave))
  await page.getByRole('button', { name: 'Confirmar desativação' }).click()
  await expect(page.getByText('Desativada')).toBeVisible()
})

// Vendedor novo, para não mexer na senha dos logins da demonstração.
test('logado, o vendedor troca a senha em Meus dados e entra com a nova', async ({ page }) => {
  const dono = await apiComo(CLIENTE_A)
  const email = `troca-${Date.now()}@revenda.dev`
  const criado = await (await dono.post('equipe', { data: { nome: 'Vendedor Troca', email } })).json() as { linkDoConvite: string }
  const token = criado.linkDoConvite.split('#')[1]
  expect((await dono.post('auth/senha', { data: { token, senha: 'senha-primeira-123' } })).ok()).toBeTruthy()
  await dono.dispose()

  await entrar(page, email, 'senha-primeira-123')
  await page.goto('/conta/dados')
  await page.getByRole('button', { name: 'Trocar senha' }).click()
  await page.getByLabel('Senha atual').fill('senha-errada-123')
  await page.getByLabel('Senha nova', { exact: true }).fill('senha-segunda-123')
  await page.getByLabel('Repita a senha nova').fill('senha-segunda-123')
  await page.getByRole('dialog').getByRole('button', { name: 'Trocar senha' }).click()
  await expect(page.getByText('Senha atual não confere.')).toBeVisible()
  await page.getByLabel('Senha atual').fill('senha-primeira-123')
  await page.getByRole('dialog').getByRole('button', { name: 'Trocar senha' }).click()
  await expect(page.getByText('Senha trocada. Os outros aparelhos saíram da conta.')).toBeVisible()
  await semViolacaoGrave(page)

  await page.reload()
  await expect(page.getByRole('heading', { name: 'Meus dados' })).toBeVisible()
  await page.context().clearCookies()
  await entrar(page, email, 'senha-segunda-123')
})
