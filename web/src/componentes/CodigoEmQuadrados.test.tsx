import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { CodigoEmQuadrados } from './CodigoEmQuadrados'

function Codigo({ aoCompletar }: { aoCompletar: (v: string) => void }) {
  const [valor, setValor] = useState('')
  return <CodigoEmQuadrados id="c" rotulo="Código" valor={valor} aoMudar={setValor} aoCompletar={aoCompletar} />
}

describe('código em quadrados', () => {
  it('aceita só dígitos, um por quadrado, e confere ao completar o sexto', async () => {
    const aoCompletar = vi.fn()
    render(<Codigo aoCompletar={aoCompletar} />)
    const campo = screen.getByLabelText('Código')
    await userEvent.type(campo, '12a34')
    expect(campo).toHaveValue('1234')
    expect(aoCompletar).not.toHaveBeenCalled()
    await userEvent.type(campo, '567')
    expect(campo).toHaveValue('123456')
    expect(aoCompletar).toHaveBeenCalledExactlyOnceWith('123456')
  })

  it('colar o código com espaço preenche os quadrados', async () => {
    const aoCompletar = vi.fn()
    render(<Codigo aoCompletar={aoCompletar} />)
    await userEvent.click(screen.getByLabelText('Código'))
    await userEvent.paste('123 456')
    expect(aoCompletar).toHaveBeenCalledWith('123456')
  })
})
