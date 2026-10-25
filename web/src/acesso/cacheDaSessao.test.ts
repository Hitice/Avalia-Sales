import { afterEach, describe, expect, it } from 'vitest'
import { consultas } from '@/api/consultas'
import { type Perfil, useSessao } from './sessao'

const perfil = (id: string, revendedor: string): Perfil => ({ id, nome: `Dono ${revendedor}`, email: `${id}@teste.com.br`, papel: 'Dono', revendedorId: `r-${id}`, revendedor, emailConfirmado: true })
const emitir = (p: Perfil) => ({ token: `t-${p.id}`, expiraEm: new Date(Date.now() + 900_000).toISOString(), perfil: p })

afterEach(() => {
  useSessao.getState().limpar()
  consultas.clear()
})

describe('cache de consultas e sessão', () => {
  it('sair esvazia o cache: a próxima pessoa do navegador não vê o que a anterior carregou', () => {
    useSessao.getState().entrar(emitir(perfil('a', 'Revenda A')))
    consultas.setQueryData(['marca'], { nomeExibido: 'Revenda A' })
    consultas.setQueryData(['vendas'], [{ cliente: 'Cliente da A' }])

    useSessao.getState().limpar()

    expect(consultas.getQueryData(['marca'])).toBeUndefined()
    expect(consultas.getQueryData(['vendas'])).toBeUndefined()
  })

  it('entrar como outra pessoa, sem ter saído (sessão que venceu), esvazia o cache', () => {
    useSessao.getState().entrar(emitir(perfil('a', 'Revenda A')))
    consultas.setQueryData(['vendas'], [{ cliente: 'Cliente da A' }])

    useSessao.getState().entrar(emitir(perfil('b', 'Revenda B')))

    expect(consultas.getQueryData(['vendas'])).toBeUndefined()
  })

  it('renovar o token da mesma pessoa mantém o cache', () => {
    useSessao.getState().entrar(emitir(perfil('a', 'Revenda A')))
    consultas.setQueryData(['vendas'], [{ cliente: 'Cliente da A' }])

    useSessao.getState().entrar(emitir(perfil('a', 'Revenda A')))

    expect(consultas.getQueryData(['vendas'])).toEqual([{ cliente: 'Cliente da A' }])
  })
})
