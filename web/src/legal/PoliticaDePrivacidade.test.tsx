import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import { PoliticaDePrivacidade } from './PoliticaDePrivacidade'
import politica from './politica-de-privacidade.md?raw'

describe('PoliticaDePrivacidade', () => {
  it('mostra o texto inteiro, com as treze seções, a tabela de dados e o contato do encarregado', () => {
    render(<MemoryRouter><PoliticaDePrivacidade /></MemoryRouter>)

    expect(screen.getByRole('heading', { level: 1, name: 'Política de Privacidade da plataforma Avalia Sales' })).toBeInTheDocument()
    expect(screen.getAllByRole('heading', { level: 2 })).toHaveLength(13)
    const tabela = screen.getByRole('table')
    expect(within(tabela).getAllByRole('columnheader').map((c) => c.textContent)).toEqual(['Dado', 'Para que usamos', 'Base legal (LGPD, art. 7º)'])
    expect(within(tabela).getAllByRole('row').length).toBeGreaterThan(8)
    expect(screen.getByText(/comercial@avaliaone\.com\.br/)).toBeInTheDocument()
    expect(screen.getAllByRole('link', { name: /Entrar|Voltar à entrada/ }).length).toBeGreaterThan(0)
  })

  it('o texto não tem travessão nem marca de rascunho, e toda promessa de prazo bate com o sistema', () => {
    expect(politica).not.toMatch(/[—–]/)
    expect(politica.toLowerCase()).not.toContain('rascunho')
    // Prazos que o código aplica: cadastro pendente 48 h, e-mail 30 dias, cópia local 14 dias e externa 30 dias.
    expect(politica).toContain('48 horas')
    expect(politica).toContain('14 dias no servidor e 30 dias no armazenamento externo')
  })
})
