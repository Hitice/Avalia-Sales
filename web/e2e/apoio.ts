import { createHmac } from 'node:crypto'
import AxeBuilder from '@axe-core/playwright'
import { type APIRequestContext, type Page, expect, request } from '@playwright/test'

/** Logins da demonstração (Demonstracao.cs), todos com a mesma senha. */
export const OPERADOR = 'operador@avalia.dev'
export const CLIENTE_A = 'cliente@a.dev'
export const CLIENTE_B = 'cliente@b.dev'
export const VENDEDOR_A = 'vendedor@a.dev'
export const VENDEDOR_B = 'vendedor@b.dev'
export const SENHA = 'demo-123'
const API = 'http://localhost:4173/api/'

export async function entrar(page: Page, email = CLIENTE_A, senha = SENHA) {
  await page.goto('/entrar')
  await page.getByLabel('E-mail').fill(email)
  await page.getByLabel('Senha').fill(senha)
  await page.getByRole('button', { name: 'Entrar' }).click()
  // A tela de entrada também tem h1: só vale quando saiu dela.
  await expect(page).not.toHaveURL(/\/entrar/)
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
}

/** Falha com violação séria ou crítica do axe (WCAG 2.2 AA), como pede o design system. */
export async function semViolacaoGrave(page: Page) {
  // Só com a tela pronta: no meio do carregamento o axe mede o que ainda está entrando.
  await page.getByRole('heading', { level: 1 }).first().waitFor()
  await page.waitForLoadState('networkidle')
  const resultado = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa']).analyze()
  const graves = resultado.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical')
  expect(graves.map((v) => `${page.url()} ${v.id}: ${v.nodes.map((n) => n.html.slice(0, 120)).slice(0, 3).join(' | ')}`)).toEqual([])
}

/** Não pode haver rolagem para o lado: a tela cabe na largura do aparelho. */
export async function semRolagemLateral(page: Page) {
  const larguras = await page.evaluate(() => [document.documentElement.scrollWidth, window.innerWidth])
  expect(larguras[0]).toBeLessThanOrEqual(larguras[1])
}

/** O código que um app autenticador mostraria agora (RFC 6238, HMAC-SHA1, 30 s). */
export function codigoTotp(chaveBase32: string, agora = Date.now()): string {
  const alfabeto = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
  let fim = chaveBase32.length
  while (chaveBase32[fim - 1] === '=') fim--
  let bits = ''
  for (const c of chaveBase32.slice(0, fim).toUpperCase()) bits += alfabeto.indexOf(c).toString(2).padStart(5, '0')
  const chave = Buffer.from((bits.match(/.{8}/g) ?? []).map((b) => Number.parseInt(b, 2)))
  const contador = Buffer.alloc(8)
  contador.writeBigInt64BE(BigInt(Math.floor(agora / 1000 / 30)))
  const hash = createHmac('sha1', chave).update(contador).digest()
  const d = hash[hash.length - 1] & 0x0f
  const numero = ((hash[d] & 0x7f) << 24) | (hash[d + 1] << 16) | (hash[d + 2] << 8) | hash[d + 3]
  return String(numero % 1_000_000).padStart(6, '0')
}

/** CNPJ válido novo a cada chamada: o cadastro recusa CNPJ repetido. */
export function cnpjNovo(): string {
  const d = Array.from({ length: 12 }, () => Math.floor(Math.random() * 10))
  for (const pesos of [[5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2], [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]]) {
    const resto = d.reduce((s, n, i) => s + n * pesos[i], 0) % 11
    d.push(resto < 2 ? 0 : 11 - resto)
  }
  return d.join('')
}

/** A API como um dos usuários da demonstração, pelo mesmo proxy do front. */
export async function apiComo(email: string): Promise<APIRequestContext> {
  const anonimo = await request.newContext({ baseURL: API })
  const resposta = await anonimo.post('auth/entrar', { data: { email, senha: SENHA } })
  expect(resposta.ok(), `entrada de ${email}`).toBeTruthy()
  const { token } = await resposta.json() as { token: string }
  await anonimo.dispose()
  return request.newContext({ baseURL: API, extraHTTPHeaders: { Authorization: `Bearer ${token}` } })
}

/** O pacote de 10 placas dos testes: não existe no produto, então o teste o cria quando falta. */
export async function pacoteDeDez(operador: APIRequestContext): Promise<string> {
  const pacotes = (await (await operador.get('operadora/pacotes')).json()) as { id: string; quantidade: number }[]
  const existente = pacotes.find((p) => p.quantidade === 10)
  if (existente) return existente.id
  const criado = await operador.post('operadora/pacotes', { data: { quantidade: 10, precoCents: 15_000 } })
  expect(criado.ok(), 'pacote de 10 do teste').toBeTruthy()
  return ((await criado.json()) as { id: string }).id
}

/**
 * Um código de placa no estoque da revenda A. Com pouco estoque, reabastece
 * pelo caminho de verdade: a operadora gera o lote, a revenda pede, a
 * operadora registra o pagamento e despacha, e a revenda ativa.
 */
export async function placaLivre(): Promise<string> {
  const dono = await apiComo(CLIENTE_A)
  const livres = async () => ((await (await dono.get('placas?estado=EmEstoque')).json()) as { itens: { codigo: string; consignadaA: string | null }[] })
    .itens.filter((p) => p.consignadaA === null)
  if ((await livres()).length < 2) {
    const operador = await apiComo(OPERADOR)
    const pacoteId = await pacoteDeDez(operador)
    const lote = (await (await operador.post('operadora/lotes', { data: { quantidade: 10 } })).json()) as { numero: number; rotulo: string }
    const pedido = (await (await dono.post('pedidos', { data: { pacoteId } })).json()) as { id: string }
    expect((await operador.post(`operadora/pedidos/${pedido.id}/pagamento`, { data: {} })).ok()).toBeTruthy()
    expect((await operador.post(`operadora/pedidos/${pedido.id}/despacho`, { data: { lote: lote.numero } })).ok()).toBeTruthy()
    expect((await dono.post('lotes/ativacao', { data: { numero: lote.rotulo } })).ok()).toBeTruthy()
    await operador.dispose()
  }
  const codigo = (await livres())[0].codigo
  await dono.dispose()
  return codigo
}

/** CPF válido novo: a venda reaproveita o cliente pelo documento, e cada teste quer o seu. */
export function cpfNovo(): string {
  const d = Array.from({ length: 9 }, () => Math.floor(Math.random() * 10))
  for (const tamanho of [9, 10]) {
    const resto = d.reduce((s, n, i) => s + n * (tamanho + 1 - i), 0) % 11
    d.push(resto < 2 ? 0 : 11 - resto)
  }
  return d.join('')
}
