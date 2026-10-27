import { render } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import { AcoesDaLinha } from './AcoesDaLinha'

function linha(outras: Parameters<typeof AcoesDaLinha>[0]['outras']) {
  const { container } = render(<MemoryRouter><AcoesDaLinha nome="Ana" principal={{ rotulo: 'Editar', aoClicar: () => {} }} outras={outras} /></MemoryRouter>)
  return container
}

describe('AcoesDaLinha', () => {
  // A linha sem "⋯" guarda a vaga dele: o botão principal fica na mesma coluna das linhas com menu (index.css).
  it('sem ações extras, guarda a vaga do menu no computador', () => {
    const container = linha([false])

    expect(container.querySelector('[data-vaga-do-menu]')).not.toBeNull()
    expect(container.querySelector('[data-com-menu]')).toBeNull()
  })

  it('com ação extra, mostra o menu no lugar da vaga', () => {
    const container = linha([{ rotulo: 'Excluir', destrutiva: true, aoClicar: () => {} }])

    expect(container.querySelector('[data-vaga-do-menu]')).toBeNull()
    expect(container.querySelector('[data-com-menu] [aria-label="Ações de Ana"]')).not.toBeNull()
  })
})
