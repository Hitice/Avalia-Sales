import { describe, expect, it, vi } from 'vitest'
import { ErroDaApi } from '@/api/cliente'
import { tentativaContinua, tentativaPara } from './tentativa'

describe('tentativa de cobrança', () => {
  it('repetida sem mudança na tela leva a mesma chave e o mesmo corpo, sem montar de novo', async () => {
    const montar = vi.fn().mockResolvedValueOnce({ token: 'tok_1' }).mockResolvedValueOnce({ token: 'tok_2' })
    const primeira = await tentativaPara(null, 'carrinho A', montar)

    const repetida = await tentativaPara(primeira, 'carrinho A', montar)

    expect(repetida).toBe(primeira)
    expect(repetida.corpo).toEqual({ token: 'tok_1' })
    expect(montar).toHaveBeenCalledTimes(1)
  })

  it('carrinho, cliente ou cartão mudado é tentativa nova, com chave nova', async () => {
    const primeira = await tentativaPara(null, 'carrinho A', async () => ({ token: 'tok_1' }))

    const nova = await tentativaPara(primeira, 'carrinho B', async () => ({ token: 'tok_2' }))

    expect(nova.chave).not.toBe(primeira.chave)
    expect(nova.corpo).toEqual({ token: 'tok_2' })
  })

  it.each([
    ['queda de rede', new TypeError('Failed to fetch'), true],
    ['409 da primeira ainda executando', new ErroDaApi(409, 'Pedido em processamento.'), true],
    ['429', new ErroDaApi(429, 'Muitas tentativas.'), true],
    ['502', new ErroDaApi(502, 'Não deu certo.'), true],
    ['422 do cartão recusado', new ErroDaApi(422, 'Cartão recusado.'), false],
    ['400', new ErroDaApi(400, 'Pedido mal formado.'), false],
  ])('%s: tentativa continua = %s', (_nome, erro, continua) => {
    expect(tentativaContinua(erro)).toBe(continua)
  })
})
