import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { ItemDoCatalogo } from '@/api/tipos'
import { ModalDaPersonalizada } from './ModalDaPersonalizada'

const comQr: ItemDoCatalogo = {
  id: 'q1', chave: 'placa-personalizada', nome: 'Placa personalizada', descricao: '', tipo: 'Servico', situacao: 'Disponivel', fotoUrl: null,
  precoSugeridoCents: 19900, precoCents: 19900, comissaoBps: null, pisoCents: 1, ativo: true, quantidadeMinima: 1, codigosNaPlaca: 2,
}

describe('ModalDaPersonalizada', () => {
  it('pede um destino por QR e só libera com todos', async () => {
    const aoConfirmar = vi.fn()
    render(<QueryClientProvider client={new QueryClient()}><ModalDaPersonalizada item={comQr} aoFechar={vi.fn()} aoConfirmar={aoConfirmar} /></QueryClientProvider>)
    const adicionar = screen.getByRole('button', { name: 'Adicionar à venda' })

    fireEvent.mouseDown(screen.getByRole('tab', { name: 'Link do cliente' }))
    fireEvent.change(await screen.findByLabelText('Endereço'), { target: { value: 'cardapio.com.br/padaria' } })
    expect(adicionar).toBeDisabled()
    fireEvent.mouseDown(screen.getByRole('tab', { name: /QR 2/ }))
    fireEvent.mouseDown(await screen.findByRole('tab', { name: 'Link do cliente' }))
    fireEvent.change(await screen.findByLabelText('Endereço'), { target: { value: 'instagram.com/padaria' } })
    fireEvent.click(adicionar)

    expect(aoConfirmar).toHaveBeenCalledOnce()
    expect(aoConfirmar.mock.calls[0][0]).toMatchObject({
      codigo: null,
      destinos: [{ tipo: 'Link', link: 'cardapio.com.br/padaria' }, { tipo: 'Link', link: 'instagram.com/padaria' }],
    })
  })
})
