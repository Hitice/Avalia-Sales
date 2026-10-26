import { isValidElement } from 'react'
import type { RouteObject } from 'react-router'
import { describe, expect, it } from 'vitest'
import { rotas } from './rotas'

/** O destino do redirecionamento de uma rota, ou undefined se ela não é um Navigate. */
function destinoDe(caminho: string, lista: readonly RouteObject[] = rotas.routes): string | undefined {
  for (const r of lista) {
    if (r.path === caminho && isValidElement<{ to: string }>(r.element)) return r.element.props.to
    const filho = r.children && destinoDe(caminho, r.children)
    if (filho) return filho
  }
}

describe('rotas movidas', () => {
  it('o razão e a marca redirecionam para as abas novas', () => {
    expect(destinoDe('razao')).toBe('/painel/financeiro?aba=lancamentos')
    expect(destinoDe('marca')).toBe('/painel/configuracoes?aba=marca')
  })
})
