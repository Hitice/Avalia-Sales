import { fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { PixNaTela } from './PixNaTela'

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('PixNaTela', () => {
  it('sem permissão para a área de transferência, avisa em vez de fingir que copiou', async () => {
    vi.stubGlobal('navigator', { ...navigator, clipboard: { writeText: () => Promise.reject(new DOMException('negado', 'NotAllowedError')) } })
    render(<PixNaTela copiaECola="00020126" imagemDoGateway="https://exemplo.com/qr.png" />)

    fireEvent.click(screen.getByRole('button', { name: 'Copiar Pix copia e cola' }))

    expect(await screen.findByText(/Não deu para copiar/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Copiado' })).toBeNull()
  })

  it('copia o código e confirma', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined)
    vi.stubGlobal('navigator', { ...navigator, clipboard: { writeText } })
    render(<PixNaTela copiaECola="00020126" imagemDoGateway="https://exemplo.com/qr.png" />)

    fireEvent.click(screen.getByRole('button', { name: 'Copiar Pix copia e cola' }))

    expect(await screen.findByRole('button', { name: 'Copiado' })).toBeInTheDocument()
    expect(writeText).toHaveBeenCalledWith('00020126')
  })
})
