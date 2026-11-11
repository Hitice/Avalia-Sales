import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import type { MesDoHistorico } from '@/api/tipos'
import { Historico } from './Graficos'

const mes = (competencia: string, recebidoCents = 0, resultadoCents = 0) => ({ competencia, recebidoCents, resultadoCents }) as MesDoHistorico

describe('histórico do financeiro', () => {
  it('sem movimento, diz isso em vez de desenhar barras vazias', () => {
    render(<Historico meses={['2026-06', '2026-07', '2026-08'].map((c) => mes(c))} />)
    expect(screen.getByText('Sem movimento ainda.')).toBeInTheDocument()
  })

  it('com menos de três meses de movimento, lista do mais recente ao mais antigo e deixa os vazios de antes de fora', () => {
    render(<Historico meses={[mes('2026-08'), mes('2026-09'), mes('2026-10', 275771, 48742), mes('2026-11', 0, -2498)]} />)
    const titulos = screen.getAllByText(/de 2026$/).map((t) => t.textContent)
    expect(titulos).toEqual(['Novembro de 2026', 'Outubro de 2026'])
    expect(screen.queryByText('Setembro de 2026')).not.toBeInTheDocument()
    expect(screen.getByText('R$ 2.757,71')).toBeInTheDocument()
  })
})
