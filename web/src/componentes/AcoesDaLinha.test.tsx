import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
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

  it('ação com confirmar só executa depois do botão do diálogo', () => {
    const baixar = vi.fn()
    render(<MemoryRouter><AcoesDaLinha nome="QR1234" principal={{ rotulo: 'Dar baixa', destrutiva: true, confirmar: 'A placa sai de circulação.', aoClicar: baixar }} /></MemoryRouter>)

    fireEvent.click(screen.getAllByRole('button', { name: 'Dar baixa' })[0])
    expect(baixar).not.toHaveBeenCalled()
    expect(screen.getByText('A placa sai de circulação.')).toBeDefined()

    fireEvent.click(screen.getByRole('button', { name: 'Voltar' }))
    expect(baixar).not.toHaveBeenCalled()

    fireEvent.click(screen.getAllByRole('button', { name: 'Dar baixa' })[0])
    const noDialogo = screen.getAllByRole('button', { name: 'Dar baixa' }).find((b) => b.closest('[role="dialog"]'))!
    fireEvent.click(noDialogo)
    expect(baixar).toHaveBeenCalledOnce()
  })
})
