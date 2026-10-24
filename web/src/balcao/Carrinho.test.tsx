import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { describe, expect, it } from 'vitest'
import type { ItemDoCatalogo } from '@/api/tipos'
import { Carrinho } from './Carrinho'
import { type LinhaDaVenda, comQuantidade, comValor, linhaDeServico, remover } from './itens'

const KIT_MESA: ItemDoCatalogo = {
  id: 's1', chave: 'kit-mesa', nome: 'Kit Mesa', descricao: '', tipo: 'Servico', situacao: 'Disponivel', fotoUrl: null,
  precoSugeridoCents: 1990, precoCents: 1990, comissaoBps: null, pisoCents: 1, ativo: true, quantidadeMinima: 10,
}

function Dono({ iniciais }: { iniciais: LinhaDaVenda[] }) {
  const [linhas, setLinhas] = useState(iniciais)
  return (
    <Carrinho
      linhas={linhas}
      aoRemover={(id) => setLinhas((l) => remover(l, id))}
      aoMudarQuantidade={(id, q) => setLinhas((l) => comQuantidade(l, id, q))}
      aoMudarValor={(id, v) => setLinhas((l) => comValor(l, id, v))}
    />
  )
}

describe('Carrinho', () => {
  it('não desce do mínimo e avisa; o mais soma', async () => {
    const usuario = userEvent.setup()
    render(<Dono iniciais={[linhaDeServico(KIT_MESA)]} />)
    const quantidade = screen.getByLabelText('Quantidade de Kit Mesa')
    expect(quantidade).toHaveValue('10')

    await usuario.click(screen.getByRole('button', { name: 'Menos Kit Mesa' }))
    expect(quantidade).toHaveValue('10')
    expect(screen.getByRole('status')).toHaveTextContent('Mínimo 10')

    await usuario.click(screen.getByRole('button', { name: 'Mais Kit Mesa' }))
    expect(quantidade).toHaveValue('11')
    expect(screen.getByTestId('total-da-venda')).toHaveTextContent('218,90')
  })

  it('digitar abaixo do mínimo volta para ele ao sair do campo', async () => {
    const usuario = userEvent.setup()
    render(<Dono iniciais={[linhaDeServico(KIT_MESA)]} />)
    const quantidade = screen.getByLabelText('Quantidade de Kit Mesa')
    await usuario.clear(quantidade)
    await usuario.type(quantidade, '4')
    await usuario.tab()
    expect(quantidade).toHaveValue('10')
  })

  it('ajusta o valor da linha e tira a linha', async () => {
    const usuario = userEvent.setup()
    render(<Dono iniciais={[linhaDeServico(KIT_MESA)]} />)
    await usuario.click(screen.getByRole('button', { name: 'Ajustar valor de Kit Mesa' }))
    await usuario.type(screen.getByLabelText('Valor cobrado de Kit Mesa'), '150,00')
    await usuario.click(screen.getByRole('button', { name: 'Aplicar' }))
    expect(screen.getByTestId('total-da-venda')).toHaveTextContent('150,00')

    await usuario.click(screen.getByRole('button', { name: 'Tirar Kit Mesa' }))
    expect(screen.getByText('Nenhum item ainda.')).toBeInTheDocument()
  })
})
